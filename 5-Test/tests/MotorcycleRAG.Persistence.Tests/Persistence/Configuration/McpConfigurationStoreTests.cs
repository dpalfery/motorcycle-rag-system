using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Persistence.Configuration;

namespace MotorcycleRAG.UnitTests.Persistence.Configuration;

public sealed class McpConfigurationStoreTests
{
    [Fact]
    public void AddOrUpdateConfiguration_AssignsIdUpdatesLookupAndRaisesEvents()
    {
        var sut = CreateSut();
        var changes = new List<McpConfigurationChangedEventArgs>();
        sut.ConfigurationChanged += (_, args) => changes.Add(args);
        var configuration = CreateConfiguration("web", "search", priority: 3);

        sut.AddOrUpdateConfiguration(configuration);
        configuration.Name = "Updated web search";
        sut.AddOrUpdateConfiguration(configuration);

        configuration.Id.Should().NotBeEmpty();
        configuration.UpdatedAt.Should().NotBeNull();
        sut.Count.Should().Be(1);
        sut.Contains(configuration.Id).Should().BeTrue();
        sut.ContainsTool("web").Should().BeTrue();
        sut.GetConfigurationById(configuration.Id).Should().BeSameAs(configuration);
        sut.GetConfigurationByToolId("web").Should().BeSameAs(configuration);
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
        sut.Invoking(store => store.AddOrUpdateConfiguration(new McpToolConfiguration())).Should().Throw<ArgumentException>();
    }

    private static McpConfigurationStore CreateSut() => new(NullLogger<McpConfigurationStore>.Instance);

    private static McpToolConfiguration CreateConfiguration(
        string toolId,
        string toolType,
        int priority,
        bool enabled = true,
        DateTime? createdAt = null) => new()
    {
        ToolId = toolId,
        Name = toolId,
        ToolType = toolType,
        IsEnabled = enabled,
        Priority = priority,
        CreatedAt = createdAt ?? DateTime.UtcNow,
    };
}
