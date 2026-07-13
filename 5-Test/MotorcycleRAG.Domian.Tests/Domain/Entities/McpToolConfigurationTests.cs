using System;
using MotorcycleRAG.Domain.Entities;
using Xunit;

namespace MotorcycleRAG.Domian.Tests.Domain.Entities;

public class McpToolConfigurationTests
{
    [Fact]
    public void Enable_SetsIsEnabledTrueAndClearsReason()
    {
        var config = new McpToolConfiguration
        {
            IsEnabled = false,
            DisabledReason = "Test reason"
        };

        config.Enable();

        Assert.True(config.IsEnabled);
        Assert.Null(config.DisabledReason);
        Assert.NotNull(config.UpdatedAt);
    }

    [Fact]
    public void Disable_SetsIsEnabledFalseAndSetsReason()
    {
        var config = new McpToolConfiguration
        {
            IsEnabled = true
        };

        var reason = "Failing connections";
        config.Disable(reason);

        Assert.False(config.IsEnabled);
        Assert.Equal(reason, config.DisabledReason);
        Assert.NotNull(config.UpdatedAt);
    }

    [Fact]
    public void UpdateConfiguration_UpdatesJsonAndTimestamp()
    {
        var config = new McpToolConfiguration();
        var json = "{\"key\":\"value\"}";

        config.UpdateConfiguration(json);

        Assert.Equal(json, config.ConfigurationJson);
        Assert.NotNull(config.UpdatedAt);
    }

    [Fact]
    public void UpdateConnectionStatus_UpdatesStatusAndTimestamps()
    {
        var config = new McpToolConfiguration();
        var status = "Connected";

        config.UpdateConnectionStatus(status);

        Assert.Equal(status, config.LastConnectionStatus);
        Assert.NotNull(config.LastTestedAt);
        Assert.NotNull(config.UpdatedAt);
    }

    [Fact]
    public void Constructor_InitializesPropertiesWithDefaults()
    {
        var config = new McpToolConfiguration();
        
        Assert.Equal(string.Empty, config.ToolId);
        Assert.Equal(string.Empty, config.Name);
        Assert.Equal(string.Empty, config.ToolType);
        Assert.NotNull(config.ServerUrl);
        Assert.True(config.IsEnabled);
        Assert.Equal(30000, config.TimeoutMs);
        Assert.True(config.RetryOnFailure);
        Assert.Equal(3, config.MaxRetries);
        Assert.True(config.CreatedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void Configuration_WhenToolIdentityAndPriorityAreSet_RetainsRoutingMetadata()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var config = new McpToolConfiguration
        {
            Id = id,
            Description = "Searches trusted service manuals.",
            Version = "2.0",
            IsSystemTool = true,
            Priority = 100
        };

        // Assert
        Assert.Equal(id, config.Id);
        Assert.Equal("Searches trusted service manuals.", config.Description);
        Assert.Equal("2.0", config.Version);
        Assert.True(config.IsSystemTool);
        Assert.Equal(100, config.Priority);
    }
}
