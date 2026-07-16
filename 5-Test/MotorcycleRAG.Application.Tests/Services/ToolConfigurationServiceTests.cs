using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
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

#pragma warning disable CA1054 // string params are intentional for InlineData flexibility
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
    [InlineData("http://172.16.0.1", false)] // private 172.16.x.x
    [InlineData("http://172.31.0.1", false)] // private 172.31.x.x
    [InlineData("http://127.1.2.3", false)] // loopback reserved range
    [InlineData("http://224.0.0.1", false)] // multicast
    [InlineData("http://255.255.255.255", false)] // broadcast
    [InlineData("http://0.0.0.0", false)] // broadcast
    [InlineData("http://example.com:8080", false)] // non-standard non-dev port
    public async Task ValidateToolAsync_VariousUrls_ReturnsExpectedResult(string url, bool expectedValid)
    {
        var config = new McpToolConfiguration { ToolId = "tool-id", ServerUrl = new Uri(url), IsEnabled = true };

        var result = await _sut.ValidateToolAsync(config);

        result.Should().Be(expectedValid);
    }

    [Fact]
    public async Task ValidateToolAsync_NullTool_ReturnsFalse()
    {
        var result = await _sut.ValidateToolAsync(null!);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateToolAsync_DisabledWithoutReason_ReturnsFalse()
    {
        var config = new McpToolConfiguration { ToolId = "tool-id", IsEnabled = false, ServerUrl = new Uri("http://localhost") };
        var result = await _sut.ValidateToolAsync(config);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateToolAsync_NullServerUrl_ReturnsFalse()
    {
        var config = new McpToolConfiguration { ToolId = "tool-id", IsEnabled = true, ServerUrl = null! };
        var result = await _sut.ValidateToolAsync(config);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateToolAsync_InvalidTimeout_ReturnsFalse()
    {
        var config = new McpToolConfiguration { ToolId = "tool-id", IsEnabled = true, ServerUrl = new Uri("http://localhost"), TimeoutMs = -1 };
        var result = await _sut.ValidateToolAsync(config);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateToolAsync_ConfigurationJsonTooLong_ReturnsFalse()
    {
        var config = new McpToolConfiguration { ToolId = "tool-id", IsEnabled = true, ServerUrl = new Uri("http://localhost"), ConfigurationJson = new string('x', 10241) };
        var result = await _sut.ValidateToolAsync(config);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateToolAsync_InvalidConfigurationJson_ReturnsFalse()
    {
        var config = new McpToolConfiguration { ToolId = "tool-id", IsEnabled = true, ServerUrl = new Uri("http://localhost"), ConfigurationJson = "{not json" };
        var result = await _sut.ValidateToolAsync(config);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetToolAsync_EmptyId_ReturnsNull()
    {
        var result = await _sut.GetToolAsync("  ");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAllToolsAsync_WhenRepositoryThrows_WrapsAndThrowsInvalidOperationException()
    {
        _configRepoMock.Setup(x => x.GetAllAsync()).ThrowsAsync(new InvalidOperationException("db error"));
        var act = () => _sut.GetAllToolsAsync();
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Error retrieving all tool configurations*");
    }

    [Fact]
    public async Task GetToolAsync_WhenRepositoryThrows_WrapsAndThrowsInvalidOperationException()
    {
        _configRepoMock.Setup(x => x.GetByToolIdAsync("tool-id")).ThrowsAsync(new InvalidOperationException("db error"));
        var act = () => _sut.GetToolAsync("tool-id");
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Error retrieving tool configuration for tool-id*");
    }

    [Fact]
    public async Task GetEnabledToolsAsync_WhenRepositoryThrows_WrapsAndThrowsInvalidOperationException()
    {
        _configRepoMock.Setup(x => x.GetEnabledAsync()).ThrowsAsync(new InvalidOperationException("db error"));
        var act = () => _sut.GetEnabledToolsAsync();
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Error retrieving enabled tool configurations*");
    }

    [Fact]
    public async Task CreateToolAsync_NullConfiguration_ThrowsArgumentNullException()
    {
        var act = () => _sut.CreateToolAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public async Task CreateToolAsync_EmptyToolId_ThrowsArgumentException()
    {
        var config = new McpToolConfiguration { ToolId = "  " };
        var act = () => _sut.CreateToolAsync(config);
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("configuration");
    }

    [Fact]
    public async Task CreateToolAsync_WhenValidationFails_ThrowsInvalidOperationException()
    {
        var config = new McpToolConfiguration { ToolId = "tool-id", IsEnabled = false, ServerUrl = null! };
        var act = () => _sut.CreateToolAsync(config);
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*Error creating MCP tool tool-id*")
            .WithInnerException<InvalidOperationException>().WithMessage("*validation failed*");
    }

    [Fact]
    public async Task CreateToolAsync_WhenToolAlreadyExists_ThrowsInvalidOperationException()
    {
        var config = new McpToolConfiguration { ToolId = "tool-id", IsEnabled = true, ServerUrl = new Uri("http://localhost") };
        _configRepoMock.Setup(x => x.GetByToolIdAsync("tool-id")).ReturnsAsync(new McpToolConfiguration { ToolId = "tool-id" });
        var act = () => _sut.CreateToolAsync(config);
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*Error creating MCP tool tool-id*")
            .WithInnerException<InvalidOperationException>().WithMessage("*already exists*");
    }

    [Fact]
    public async Task CreateToolAsync_GeneratesIdWhenEmpty()
    {
        var config = new McpToolConfiguration { ToolId = "tool-id", IsEnabled = true, ServerUrl = new Uri("http://localhost") };
        _configRepoMock.Setup(x => x.GetByToolIdAsync("tool-id")).ReturnsAsync((McpToolConfiguration?)null);
        McpToolConfiguration? saved = null;
        _configRepoMock.Setup(x => x.AddOrUpdateAsync(It.IsAny<McpToolConfiguration>())).ReturnsAsync((McpToolConfiguration c) => { saved = c; return c; });

        await _sut.CreateToolAsync(config);

        saved.Should().NotBeNull();
        saved!.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task CreateToolAsync_WhenRepositoryThrows_WrapsAndThrowsInvalidOperationException()
    {
        var config = new McpToolConfiguration { ToolId = "tool-id", IsEnabled = true, ServerUrl = new Uri("http://localhost") };
        _configRepoMock.Setup(x => x.GetByToolIdAsync("tool-id")).ReturnsAsync((McpToolConfiguration?)null);
        _configRepoMock.Setup(x => x.AddOrUpdateAsync(It.IsAny<McpToolConfiguration>())).ThrowsAsync(new InvalidOperationException("db error"));
        var act = () => _sut.CreateToolAsync(config);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Error creating MCP tool tool-id*");
    }

    [Fact]
    public async Task UpdateToolAsync_EmptyToolId_ThrowsArgumentException()
    {
        var act = () => _sut.UpdateToolAsync("  ", new McpToolConfiguration { ToolId = "tool-id" });
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("toolId");
    }

    [Fact]
    public async Task UpdateToolAsync_NullConfiguration_ThrowsArgumentNullException()
    {
        var act = () => _sut.UpdateToolAsync("tool-id", null!);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public async Task UpdateToolAsync_WhenToolNotFound_ThrowsInvalidOperationException()
    {
        var config = new McpToolConfiguration { ToolId = "tool-id", IsEnabled = true, ServerUrl = new Uri("http://localhost") };
        _configRepoMock.Setup(x => x.GetByToolIdAsync("tool-id")).ReturnsAsync((McpToolConfiguration?)null);
        var act = () => _sut.UpdateToolAsync("tool-id", config);
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*Error updating MCP tool tool-id*")
            .WithInnerException<InvalidOperationException>().WithMessage("*not found*");
    }

    [Fact]
    public async Task UpdateToolAsync_WhenValidationFails_ThrowsInvalidOperationException()
    {
        var config = new McpToolConfiguration { ToolId = "tool-id", IsEnabled = false, ServerUrl = null! };
        _configRepoMock.Setup(x => x.GetByToolIdAsync("tool-id")).ReturnsAsync(new McpToolConfiguration { ToolId = "tool-id" });
        var act = () => _sut.UpdateToolAsync("tool-id", config);
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*Error updating MCP tool tool-id*")
            .WithInnerException<InvalidOperationException>().WithMessage("*validation failed*");
    }

    [Fact]
    public async Task UpdateToolAsync_PreservesExistingToolIdAndCreatedAt()
    {
        var id = Guid.NewGuid();
        var createdAt = DateTime.UtcNow.AddDays(-1);
        var existing = new McpToolConfiguration { Id = id, ToolId = "old-tool-id", CreatedAt = createdAt, IsEnabled = true, ServerUrl = new Uri("http://localhost") };
        var updated = new McpToolConfiguration { ToolId = "new-tool-id", Name = "New", IsEnabled = true, ServerUrl = new Uri("http://localhost") };
        _configRepoMock.Setup(x => x.GetByToolIdAsync("old-tool-id")).ReturnsAsync(existing);
        McpToolConfiguration? saved = null;
        _configRepoMock.Setup(x => x.AddOrUpdateAsync(It.IsAny<McpToolConfiguration>())).ReturnsAsync((McpToolConfiguration c) => { saved = c; return c; });

        await _sut.UpdateToolAsync("old-tool-id", updated);

        saved.Should().NotBeNull();
        saved!.Id.Should().Be(id);
        saved.ToolId.Should().Be("old-tool-id");
        saved.CreatedAt.Should().Be(createdAt);
        saved.UpdatedAt.Should().BeOnOrAfter(DateTime.UtcNow.AddSeconds(-1));
    }

    [Fact]
    public async Task EnableToolAsync_EmptyToolId_ThrowsArgumentException()
    {
        var act = () => _sut.EnableToolAsync("  ");
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("toolId");
    }

    [Fact]
    public async Task EnableToolAsync_WhenToolNotFound_ThrowsInvalidOperationException()
    {
        _configRepoMock.Setup(x => x.GetByToolIdAsync("tool-id")).ReturnsAsync((McpToolConfiguration?)null);
        var act = () => _sut.EnableToolAsync("tool-id");
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*Error enabling MCP tool tool-id*")
            .WithInnerException<InvalidOperationException>().WithMessage("*not found*");
    }

    [Fact]
    public async Task EnableToolAsync_WhenRepositoryThrows_WrapsAndThrowsInvalidOperationException()
    {
        _configRepoMock.Setup(x => x.GetByToolIdAsync("tool-id")).ThrowsAsync(new InvalidOperationException("db error"));
        var act = () => _sut.EnableToolAsync("tool-id");
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Error enabling MCP tool tool-id*");
    }

    [Fact]
    public async Task DisableToolAsync_EmptyToolId_ThrowsArgumentException()
    {
        var act = () => _sut.DisableToolAsync("  ", "reason");
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("toolId");
    }

    [Fact]
    public async Task DisableToolAsync_EmptyReason_ThrowsArgumentException()
    {
        var act = () => _sut.DisableToolAsync("tool-id", "  ");
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("reason");
    }

    [Fact]
    public async Task DisableToolAsync_WhenToolNotFound_ThrowsInvalidOperationException()
    {
        _configRepoMock.Setup(x => x.GetByToolIdAsync("tool-id")).ReturnsAsync((McpToolConfiguration?)null);
        var act = () => _sut.DisableToolAsync("tool-id", "reason");
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*Error disabling MCP tool tool-id*")
            .WithInnerException<InvalidOperationException>().WithMessage("*not found*");
    }

    [Fact]
    public async Task DisableToolAsync_WhenRepositoryThrows_WrapsAndThrowsInvalidOperationException()
    {
        _configRepoMock.Setup(x => x.GetByToolIdAsync("tool-id")).ThrowsAsync(new InvalidOperationException("db error"));
        var act = () => _sut.DisableToolAsync("tool-id", "reason");
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Error disabling MCP tool tool-id*");
    }

    [Fact]
    public async Task DeleteToolAsync_EmptyToolId_ThrowsArgumentException()
    {
        var act = () => _sut.DeleteToolAsync("  ");
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("toolId");
    }

    [Fact]
    public async Task DeleteToolAsync_WhenToolNotFound_ReturnsFalse()
    {
        _configRepoMock.Setup(x => x.GetByToolIdAsync("tool-id")).ReturnsAsync((McpToolConfiguration?)null);
        var result = await _sut.DeleteToolAsync("tool-id");
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteToolAsync_WhenDeleteFails_ReturnsFalse()
    {
        var config = new McpToolConfiguration { ToolId = "tool-id", Id = Guid.NewGuid() };
        _configRepoMock.Setup(x => x.GetByToolIdAsync("tool-id")).ReturnsAsync(config);
        _configRepoMock.Setup(x => x.DeleteAsync(config.Id)).ReturnsAsync(false);
        var result = await _sut.DeleteToolAsync("tool-id");
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteToolAsync_WhenRepositoryThrows_WrapsAndThrowsInvalidOperationException()
    {
        _configRepoMock.Setup(x => x.GetByToolIdAsync("tool-id")).ThrowsAsync(new InvalidOperationException("db error"));
        var act = () => _sut.DeleteToolAsync("tool-id");
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Error deleting MCP tool tool-id*");
    }

    [Fact]
    public async Task GetAuditHistoryAsync_ReturnsEntries()
    {
        var id = Guid.NewGuid();
        _auditRepoMock.Setup(x => x.GetAuditHistoryAsync(id, 50)).ReturnsAsync([]);
        var result = await _sut.GetAuditHistoryAsync(id, 50);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAuditHistoryAsync_EmptyConfigId_ThrowsArgumentException()
    {
        var act = () => _sut.GetAuditHistoryAsync(Guid.Empty, 50);
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("configId");
    }

    [Fact]
    public async Task GetAuditHistoryAsync_InvalidLimit_ThrowsArgumentException()
    {
        var act = () => _sut.GetAuditHistoryAsync(Guid.NewGuid(), 0);
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("limit");
    }

    [Fact]
    public async Task GetAuditHistoryAsync_WhenRepositoryThrows_WrapsAndThrowsInvalidOperationException()
    {
        var id = Guid.NewGuid();
        _auditRepoMock.Setup(x => x.GetAuditHistoryAsync(id, 50)).ThrowsAsync(new InvalidOperationException("db error"));
        var act = () => _sut.GetAuditHistoryAsync(id, 50);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Error retrieving audit history*");
    }

    [Fact]
    public async Task GetAuditSummaryAsync_ReturnsSummary()
    {
        _auditRepoMock.Setup(x => x.GetAuditSummaryAsync()).ReturnsAsync(new ToolConfigurationAuditSummary());
        var result = await _sut.GetAuditSummaryAsync();
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetAuditSummaryAsync_WhenRepositoryThrows_WrapsAndThrowsInvalidOperationException()
    {
        _auditRepoMock.Setup(x => x.GetAuditSummaryAsync()).ThrowsAsync(new InvalidOperationException("db error"));
        var act = () => _sut.GetAuditSummaryAsync();
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Error retrieving audit summary*");
    }

    [Fact]
    public async Task GetAuditEntriesByActionAsync_ReturnsEntries()
    {
        _auditRepoMock.Setup(x => x.GetAuditEntriesByActionAsync("created")).ReturnsAsync([]);
        var result = await _sut.GetAuditEntriesByActionAsync("created");
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAuditEntriesByActionAsync_EmptyAction_ThrowsArgumentException()
    {
        var act = () => _sut.GetAuditEntriesByActionAsync("  ");
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("action");
    }

    [Fact]
    public async Task GetAuditEntriesByActionAsync_WhenRepositoryThrows_WrapsAndThrowsInvalidOperationException()
    {
        _auditRepoMock.Setup(x => x.GetAuditEntriesByActionAsync("created")).ThrowsAsync(new InvalidOperationException("db error"));
        var act = () => _sut.GetAuditEntriesByActionAsync("created");
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Error retrieving audit entries*");
    }
}
