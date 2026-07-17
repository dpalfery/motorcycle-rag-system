using System;
using MotorcycleRAG.Domain.Entities;
using Xunit;

namespace MotorcycleRAG.Domian.Tests.Domain.Entities;

public class McpToolConfigurationTests
{
    private static readonly Uri ValidServerUrl = new("https://mcp.example.test");

    [Fact]
    public void Enable_SetsIsEnabledTrueAndClearsReason()
    {
        var config = RehydrateConfig(isEnabled: false, disabledReason: "Test reason");

        config.Enable();

        Assert.True(config.IsEnabled);
        Assert.Null(config.DisabledReason);
        Assert.NotNull(config.UpdatedAt);
    }

    [Fact]
    public void Disable_SetsIsEnabledFalseAndSetsReason()
    {
        var config = RehydrateConfig(isEnabled: true);

        var reason = "Failing connections";
        config.Disable(reason);

        Assert.False(config.IsEnabled);
        Assert.Equal(reason, config.DisabledReason);
        Assert.NotNull(config.UpdatedAt);
    }

    [Fact]
    public void UpdateConfiguration_UpdatesJsonAndTimestamp()
    {
        var config = RehydrateConfig();
        var json = "{\"key\":\"value\"}";

        config.UpdateConfiguration(json);

        Assert.Equal(json, config.ConfigurationJson);
        Assert.NotNull(config.UpdatedAt);
    }

    [Fact]
    public void UpdateConnectionStatus_UpdatesStatusAndTimestamps()
    {
        var config = RehydrateConfig();
        var status = "Connected";

        config.UpdateConnectionStatus(status);

        Assert.Equal(status, config.LastConnectionStatus);
        Assert.NotNull(config.LastTestedAt);
        Assert.NotNull(config.UpdatedAt);
    }

    [Fact]
    public void Create_AppliesApprovedDefaultsForNewConfigurations()
    {
        var before = DateTime.UtcNow;
        var config = McpToolConfiguration.Create(
            "search",
            "Search",
            ValidServerUrl,
            "search");

        Assert.NotEqual(Guid.Empty, config.Id);
        Assert.Equal("search", config.ToolId);
        Assert.Equal("Search", config.Name);
        Assert.True(config.IsEnabled);
        Assert.Equal(30_000, config.TimeoutMs);
        Assert.True(config.RetryOnFailure);
        Assert.Equal(3, config.MaxRetries);
        Assert.False(config.IsSystemTool);
        Assert.Equal(0, config.Priority);
        Assert.InRange(config.CreatedAt, before, DateTime.UtcNow);
        Assert.Null(config.UpdatedAt);
    }

    [Fact]
    public void Create_ValidatesRequiredToolIdentity()
    {
        var config = McpToolConfiguration.Create(
            "search",
            "Search",
            ValidServerUrl,
            "search");

        Assert.Equal("search", config.ToolId);
        Assert.True(config.IsEnabled);
        Assert.Equal(30_000, config.TimeoutMs);
    }

    [Fact]
    public void Disable_RejectsBlankReasonAndNormalizesValidReason()
    {
        var config = RehydrateConfig();

        Assert.Throws<ArgumentException>(() => config.Disable(" "));

        config.Disable("  maintenance  ");

        Assert.False(config.IsEnabled);
        Assert.Equal("maintenance", config.DisabledReason);
    }

    [Fact]
    public void Rehydrate_PreservesAllPersistedFields()
    {
        var id = Guid.NewGuid();
        var createdAt = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc);
        var updatedAt = new DateTime(2026, 7, 9, 9, 0, 0, DateTimeKind.Utc);

        var config = McpToolConfiguration.Rehydrate(
            id: id,
            toolId: "search",
            name: "Search",
            description: "Searches trusted service manuals.",
            serverUrl: ValidServerUrl,
            toolType: "search",
            version: "2.0",
            isSystemTool: true,
            priority: 100,
            timeoutMs: 1500,
            retryOnFailure: false,
            maxRetries: 5,
            createdAt: createdAt,
            isEnabled: false,
            configurationJson: "{\"mode\":\"fast\"}",
            disabledReason: "Maintenance",
            lastTestedAt: createdAt,
            lastConnectionStatus: "Unhealthy",
            updatedAt: updatedAt);

        Assert.Equal(id, config.Id);
        Assert.Equal("Searches trusted service manuals.", config.Description);
        Assert.Equal("2.0", config.Version);
        Assert.True(config.IsSystemTool);
        Assert.Equal(100, config.Priority);
        Assert.False(config.IsEnabled);
        Assert.Equal("Maintenance", config.DisabledReason);
        Assert.Equal("{\"mode\":\"fast\"}", config.ConfigurationJson);
        Assert.Equal(updatedAt, config.UpdatedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rehydrate_WithMissingToolId_RejectsTheRow(string toolId)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            McpToolConfiguration.Rehydrate(
                id: Guid.NewGuid(),
                toolId: toolId,
                name: "Search",
                description: null,
                serverUrl: ValidServerUrl,
                toolType: "search",
                version: null,
                isSystemTool: false,
                priority: 0,
                timeoutMs: 30000,
                retryOnFailure: true,
                maxRetries: 3,
                createdAt: DateTime.UtcNow,
                isEnabled: true,
                configurationJson: null,
                disabledReason: null,
                lastTestedAt: null,
                lastConnectionStatus: null,
                updatedAt: null));
        Assert.Equal("toolId", ex.ParamName);
    }

    [Fact]
    public void Rehydrate_WithEmptyId_RejectsTheRow()
    {
        var ex = Assert.Throws<ArgumentException>(() => RehydrateConfig(id: Guid.Empty));
        Assert.Equal("id", ex.ParamName);
    }

    [Fact]
    public void Rehydrate_WithMissingName_RejectsTheRow()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            McpToolConfiguration.Rehydrate(
                id: Guid.NewGuid(),
                toolId: "search",
                name: "  ",
                description: null,
                serverUrl: ValidServerUrl,
                toolType: "search",
                version: null,
                isSystemTool: false,
                priority: 0,
                timeoutMs: 30000,
                retryOnFailure: true,
                maxRetries: 3,
                createdAt: DateTime.UtcNow,
                isEnabled: true,
                configurationJson: null,
                disabledReason: null,
                lastTestedAt: null,
                lastConnectionStatus: null,
                updatedAt: null));
        Assert.Equal("name", ex.ParamName);
    }

    [Fact]
    public void Rehydrate_WithMissingToolType_RejectsTheRow()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            McpToolConfiguration.Rehydrate(
                id: Guid.NewGuid(),
                toolId: "search",
                name: "Search",
                description: null,
                serverUrl: ValidServerUrl,
                toolType: "",
                version: null,
                isSystemTool: false,
                priority: 0,
                timeoutMs: 30000,
                retryOnFailure: true,
                maxRetries: 3,
                createdAt: DateTime.UtcNow,
                isEnabled: true,
                configurationJson: null,
                disabledReason: null,
                lastTestedAt: null,
                lastConnectionStatus: null,
                updatedAt: null));
        Assert.Equal("toolType", ex.ParamName);
    }

    [Fact]
    public void Rehydrate_AcceptsNullServerUrlForPersistenceHydration()
    {
        // ServerUrl is a per-instance invariant only when set via Create; persisted
        // rows may be hydrated without one and ValidateToolAsync rejects them at runtime.
        var config = McpToolConfiguration.Rehydrate(
            id: Guid.NewGuid(),
            toolId: "search",
            name: "Search",
            description: null,
            serverUrl: null,
            toolType: "search",
            version: null,
            isSystemTool: false,
            priority: 0,
            timeoutMs: 30000,
            retryOnFailure: true,
            maxRetries: 3,
            createdAt: DateTime.UtcNow,
            isEnabled: true,
            configurationJson: null,
            disabledReason: null,
            lastTestedAt: null,
            lastConnectionStatus: null,
            updatedAt: null);

        Assert.Null(config.ServerUrl);
    }

    /// <summary>
    /// Builds a fully-formed config for behavior tests. Defaults mirror Create's defaults
    /// but allow transition-state overrides so tests do not depend on illegal setter access.
    /// </summary>
    private static McpToolConfiguration RehydrateConfig(
        Guid? id = null,
        bool isEnabled = true,
        string? disabledReason = null) =>
        McpToolConfiguration.Rehydrate(
            id: id ?? Guid.NewGuid(),
            toolId: "search",
            name: "Search",
            description: null,
            serverUrl: ValidServerUrl,
            toolType: "search",
            version: null,
            isSystemTool: false,
            priority: 0,
            timeoutMs: 30000,
            retryOnFailure: true,
            maxRetries: 3,
            createdAt: DateTime.UtcNow,
            isEnabled: isEnabled,
            configurationJson: null,
            disabledReason: disabledReason,
            lastTestedAt: null,
            lastConnectionStatus: null,
            updatedAt: null);
}
