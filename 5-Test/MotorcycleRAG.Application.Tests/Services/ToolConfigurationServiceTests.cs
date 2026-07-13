using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Entities;
using Xunit;

namespace MotorcycleRAG.UnitTests.Services;

public class ToolConfigurationServiceTests
{
    private readonly Mock<IToolConfigurationRepository> _configRepoMock;
    private readonly Mock<IToolConfigurationAuditRepository> _auditRepoMock;
    private readonly ToolConfigurationService _sut;

    public ToolConfigurationServiceTests()
    {
        _configRepoMock = new Mock<IToolConfigurationRepository>();
        _auditRepoMock = new Mock<IToolConfigurationAuditRepository>();
        _sut = new ToolConfigurationService(_configRepoMock.Object, _auditRepoMock.Object, NullLogger<ToolConfigurationService>.Instance);
    }

    [Fact]
    public void Constructor_NullDependencies_ThrowsArgumentNullException()
    {
        var logger = NullLogger<ToolConfigurationService>.Instance;
        Assert.Throws<ArgumentNullException>(() => new ToolConfigurationService(null!, _auditRepoMock.Object, logger));
        Assert.Throws<ArgumentNullException>(() => new ToolConfigurationService(_configRepoMock.Object, null!, logger));
        Assert.Throws<ArgumentNullException>(() => new ToolConfigurationService(_configRepoMock.Object, _auditRepoMock.Object, null!));
    }

    [Fact]
    public async Task GetAllToolsAsync_ReturnsOrderedConfigs()
    {
        var config1 = new McpToolConfiguration { ToolId = "1", CreatedAt = DateTime.UtcNow.AddMinutes(-5) };
        var config2 = new McpToolConfiguration { ToolId = "2", CreatedAt = DateTime.UtcNow.AddMinutes(-10) };
        _configRepoMock.Setup(x => x.GetAllAsync()).ReturnsAsync(new[] { config1, config2 });

        var result = await _sut.GetAllToolsAsync();

        result.Should().HaveCount(2);
        result[0].ToolId.Should().Be("2");
        result[1].ToolId.Should().Be("1");
    }

    [Fact]
    public async Task GetToolAsync_ReturnsConfig()
    {
        var config = new McpToolConfiguration { ToolId = "tool-id" };
        _configRepoMock.Setup(x => x.GetByToolIdAsync("tool-id")).ReturnsAsync(config);

        var result = await _sut.GetToolAsync("tool-id");

        result.Should().Be(config);
    }

    [Fact]
    public async Task GetEnabledToolsAsync_ReturnsEnabledConfigs()
    {
        var config = new McpToolConfiguration { ToolId = "tool-id", IsEnabled = true };
        _configRepoMock.Setup(x => x.GetEnabledAsync()).ReturnsAsync(new[] { config });

        var result = await _sut.GetEnabledToolsAsync();

        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task CreateToolAsync_WithValidConfig_CreatesToolAndAudit()
    {
        var config = new McpToolConfiguration { ToolId = "tool-id", Name = "Tool", ServerUrl = new Uri("http://localhost:8000") };
        _configRepoMock.Setup(x => x.GetByToolIdAsync("tool-id")).ReturnsAsync((McpToolConfiguration?)null);
        _configRepoMock.Setup(x => x.AddOrUpdateAsync(It.IsAny<McpToolConfiguration>())).ReturnsAsync(config);

        var result = await _sut.CreateToolAsync(config, "user1");

        result.Should().Be(config);
        _configRepoMock.Verify(x => x.AddOrUpdateAsync(It.IsAny<McpToolConfiguration>()), Times.Once);
        _auditRepoMock.Verify(x => x.RecordChangeAsync(It.IsAny<Guid>(), "tool-id", "created", null, It.IsAny<string>(), "user1", It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task UpdateToolAsync_WithValidConfig_UpdatesToolAndAudit()
    {
        var existingConfig = new McpToolConfiguration { ToolId = "tool-id", Name = "Old", ServerUrl = new Uri("http://localhost:8000") };
        var updatedConfig = new McpToolConfiguration { ToolId = "tool-id", Name = "New", ServerUrl = new Uri("http://localhost:8000") };
        
        _configRepoMock.Setup(x => x.GetByToolIdAsync("tool-id")).ReturnsAsync(existingConfig);
        _configRepoMock.Setup(x => x.AddOrUpdateAsync(It.IsAny<McpToolConfiguration>())).ReturnsAsync(updatedConfig);

        var result = await _sut.UpdateToolAsync("tool-id", updatedConfig, "Updated", "user1");

        result.Should().Be(updatedConfig);
        _configRepoMock.Verify(x => x.AddOrUpdateAsync(It.IsAny<McpToolConfiguration>()), Times.Once);
        _auditRepoMock.Verify(x => x.RecordChangeAsync(It.IsAny<Guid>(), "tool-id", "updated", It.IsAny<string>(), It.IsAny<string>(), "user1", "Updated"), Times.Once);
    }

    [Fact]
    public async Task EnableToolAsync_WithValidId_EnablesToolAndAudit()
    {
        var config = new McpToolConfiguration { ToolId = "tool-id", IsEnabled = false };
        _configRepoMock.Setup(x => x.GetByToolIdAsync("tool-id")).ReturnsAsync(config);
        _configRepoMock.Setup(x => x.AddOrUpdateAsync(It.IsAny<McpToolConfiguration>())).ReturnsAsync(config);

        var result = await _sut.EnableToolAsync("tool-id", "user1");

        result.IsEnabled.Should().BeTrue();
        _configRepoMock.Verify(x => x.AddOrUpdateAsync(It.IsAny<McpToolConfiguration>()), Times.Once);
        _auditRepoMock.Verify(x => x.RecordChangeAsync(It.IsAny<Guid>(), "tool-id", "enabled", It.IsAny<string>(), It.IsAny<string>(), "user1", It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task DisableToolAsync_WithValidId_DisablesToolAndAudit()
    {
        var config = new McpToolConfiguration { ToolId = "tool-id", IsEnabled = true };
        _configRepoMock.Setup(x => x.GetByToolIdAsync("tool-id")).ReturnsAsync(config);
        _configRepoMock.Setup(x => x.AddOrUpdateAsync(It.IsAny<McpToolConfiguration>())).ReturnsAsync(config);

        var result = await _sut.DisableToolAsync("tool-id", "Testing", "user1");

        result.IsEnabled.Should().BeFalse();
        result.DisabledReason.Should().Be("Testing");
        _configRepoMock.Verify(x => x.AddOrUpdateAsync(It.IsAny<McpToolConfiguration>()), Times.Once);
        _auditRepoMock.Verify(x => x.RecordChangeAsync(It.IsAny<Guid>(), "tool-id", "disabled", It.IsAny<string>(), It.IsAny<string>(), "user1", "Testing"), Times.Once);
    }

    [Fact]
    public async Task DeleteToolAsync_WithValidId_DeletesToolAndAudit()
    {
        var config = new McpToolConfiguration { ToolId = "tool-id", Id = Guid.NewGuid() };
        _configRepoMock.Setup(x => x.GetByToolIdAsync("tool-id")).ReturnsAsync(config);
        _configRepoMock.Setup(x => x.DeleteAsync(config.Id)).ReturnsAsync(true);

        var result = await _sut.DeleteToolAsync("tool-id", "user1");

        result.Should().BeTrue();
        _configRepoMock.Verify(x => x.DeleteAsync(config.Id), Times.Once);
        _auditRepoMock.Verify(x => x.RecordChangeAsync(config.Id, "tool-id", "deleted", It.IsAny<string>(), null, "user1", It.IsAny<string>()), Times.Once);
    }

    [Theory]
    [InlineData("http://localhost", true)]
    [InlineData("https://localhost:443", true)]
    [InlineData("http://127.0.0.1:8000", true)]
    [InlineData("http://[::1]:8001", false)]
    [InlineData("http://example.com", true)]
    [InlineData("https://example.com:443", true)]
    [InlineData("http://10.0.0.1", false)] // private ip
    [InlineData("http://192.168.1.1", false)] // private ip
    [InlineData("http://169.254.1.1", false)] // link-local
    [InlineData("http://example.com:8000", false)] // dev port on non-localhost
    [InlineData("ftp://localhost", false)] // invalid scheme
    public async Task ValidateToolAsync_VariousUrls_ReturnsExpectedResult(string url, bool expectedValid)
    {
        var config = new McpToolConfiguration { ToolId = "tool-id", ServerUrl = new Uri(url), IsEnabled = true };
        
        var result = await _sut.ValidateToolAsync(config);

        result.Should().Be(expectedValid);
    }
}
