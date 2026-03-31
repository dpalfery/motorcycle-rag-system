using Microsoft.Extensions.Logging;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Entities;

namespace MotorcycleRAG.UnitTests.Services;

public class ToolConfigurationServiceTests
{
    private readonly Mock<IToolConfigurationRepository> _configRepository = new();
    private readonly Mock<IToolConfigurationAuditRepository> _auditRepository = new();
    private readonly Mock<ILogger<ToolConfigurationService>> _logger = new();

    [Fact]
    public async Task ValidateToolAsync_LocalhostDevelopmentPort_ReturnsTrue()
    {
        var service = CreateService();
        var tool = new McpToolConfiguration
        {
            Id = Guid.NewGuid(),
            ToolId = "local-tool",
            Name = "Local Tool",
            ToolType = "processor",
            IsEnabled = true,
            ServerUrl = new Uri("http://localhost:8100"),
            CreatedAt = DateTime.UtcNow
        };

        var result = await service.ValidateToolAsync(tool);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateToolAsync_NonLocalhostDevelopmentPort_ReturnsFalse()
    {
        var service = CreateService();
        var tool = new McpToolConfiguration
        {
            Id = Guid.NewGuid(),
            ToolId = "remote-tool",
            Name = "Remote Tool",
            ToolType = "processor",
            IsEnabled = true,
            ServerUrl = new Uri("http://example.com:8000"),
            CreatedAt = DateTime.UtcNow
        };

        var result = await service.ValidateToolAsync(tool);

        result.Should().BeFalse();
    }

    private ToolConfigurationService CreateService()
    {
        return new ToolConfigurationService(_configRepository.Object, _auditRepository.Object, _logger.Object);
    }
}
