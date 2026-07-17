using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Persistence.Configuration;

namespace MotorcycleRAG.UnitTests.Persistence.Configuration;

public sealed class McpConfigurationStoreTests
{
    [Fact]
    public void AddOrUpdateConfiguration_UpdatesLookupAndRaisesEvents()
    {
        var sut = CreateSut();
        var changes = new List<McpConfigurationChangedEventArgs>();
        sut.ConfigurationChanged += (_, args) => changes.Add(args);
        var configuration = CreateConfiguration("web", "search", priority: 3);

        sut.AddOrUpdateConfiguration(configuration);
        // Callers assign a persistence identity once, upstream, so an "update" is
        // represented by a fresh snapshot carrying the same Id (McpToolConfiguration
        // is immutable outside its own named domain transitions).
        var updated = CreateConfiguration("web", "search", priority: 3, id: configuration.Id, name: "Updated web search");
        sut.AddOrUpdateConfiguration(updated);

        configuration.Id.Should().NotBeEmpty();
        sut.Count.Should().Be(1);
        sut.Contains(configuration.Id).Should().BeTrue();
        sut.ContainsTool("web").Should().BeTrue();
        sut.GetConfigurationById(configuration.Id).Should().BeSameAs(updated);
        sut.GetConfigurationByToolId("web").Should().BeSameAs(updated);
        changes.Select(change => change.ChangeType).Should().Equal("added", "updated");
        sut.LastUpdated.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void ConfigurationQueries_FilterAndOrderEnabledConfigurations()
    {
        var sut = CreateSut();
        var late = CreateConfiguration("late", "search", priority: 10, createdAt: DateTime.UtcNow.AddMinutes(2));
        var early = CreateConfiguration("early", "search", priority: 1, createdAt: DateTime.UtcNow.AddMinutes(-2));
        var disabled = CreateConfiguration("disabled", "processor", priority: 0, enabled: false);
        sut.AddOrUpdateConfiguration(late);
        sut.AddOrUpdateConfiguration(early);
        sut.AddOrUpdateConfiguration(disabled);

        sut.GetAllConfigurations().Select(configuration => configuration.ToolId).Should().Equal("early", "disabled", "late");
        sut.GetEnabledConfigurations().Select(configuration => configuration.ToolId).Should().Equal("early", "late");
        sut.GetConfigurationsByType("search").Select(configuration => configuration.ToolId).Should().Equal("early", "late");
        sut.GetConfigurationsByType(" ").Should().BeEmpty();
        sut.GetConfigurationByToolId(" ").Should().BeNull();
    }

    [Fact]
    public void RemoveAndClear_RaiseEventsAndRemoveLookupEntries()
    {
        var sut = CreateSut();
        var first = CreateConfiguration("first", "search", 1);
        var second = CreateConfiguration("second", "search", 2);
        var changes = new List<McpConfigurationChangedEventArgs>();
        sut.ConfigurationChanged += (_, args) => changes.Add(args);
        sut.AddOrUpdateConfiguration(first);
        sut.AddOrUpdateConfiguration(second);

        sut.RemoveConfiguration(first.Id).Should().BeTrue();
        sut.RemoveConfiguration(first.Id).Should().BeFalse();
        sut.ContainsTool("first").Should().BeFalse();
        sut.Clear();

        sut.Count.Should().Be(0);
        sut.ContainsTool("second").Should().BeFalse();
        changes.Select(change => change.ChangeType).Should().Equal("added", "added", "removed", "cleared");
        changes.Last().ConfigurationId.Should().BeEmpty();
    }

    [Fact]
    public void ConstructorAndAddOrUpdate_WithInvalidArguments_Throw()
    {
        var constructor = () => new McpConfigurationStore(null!);
        constructor.Should().Throw<ArgumentNullException>();
        var sut = CreateSut();

        sut.Invoking(store => store.AddOrUpdateConfiguration(null!)).Should().Throw<ArgumentNullException>();
        // The entity's Rehydrate factory now rejects empty identity fields at
        // construction, so the store's defense-in-depth check for empty Id/ToolId
        // is no longer reachable through public construction. A validly-constructed
        // instance is the minimum the store can receive.
    }

    private static McpConfigurationStore CreateSut() => new(NullLogger<McpConfigurationStore>.Instance);

    private static McpToolConfiguration CreateConfiguration(
        string toolId,
        string toolType,
        int priority,
        bool enabled = true,
        DateTime? createdAt = null,
        Guid? id = null,
        string? name = null) =>
        McpToolConfiguration.Rehydrate(
            id: id ?? Guid.NewGuid(),
            toolId: toolId,
            name: name ?? toolId,
            description: null,
            serverUrl: new Uri("https://mcp.example.test"),
            toolType: toolType,
            version: null,
            isSystemTool: false,
            priority: priority,
            timeoutMs: 30000,
            retryOnFailure: true,
            maxRetries: 3,
            createdAt: createdAt ?? DateTime.UtcNow,
            isEnabled: enabled,
            configurationJson: null,
            disabledReason: enabled ? null : "Disabled",
            lastTestedAt: null,
            lastConnectionStatus: null,
            updatedAt: null);
}
