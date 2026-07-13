using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Persistence.Telemetry;

namespace MotorcycleRAG.UnitTests.Persistence.Telemetry;

public sealed class BudgetMonitorServiceTests
{
    [Fact]
    public async Task RecordSpendAsync_TracksSpendAndEnforcesConfiguredLimit()
    {
        var sut = CreateSut(monthlyLimit: 100m);

        sut.IsWithinBudget(100m).Should().BeTrue();
        await sut.RecordSpendAsync(80m, "ingestion\nrequest");

        (await sut.GetMonthlySpendAsync()).Should().Be(80m);
        sut.IsWithinBudget(20m).Should().BeTrue();
        sut.IsWithinBudget(20.01m).Should().BeFalse();
    }

    [Fact]
    public async Task RecordSpendAsync_WithCancelledToken_ThrowsWithoutRecordingSpend()
    {
        var sut = CreateSut();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var act = () => sut.RecordSpendAsync(10m, "operation", cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        (await sut.GetMonthlySpendAsync()).Should().Be(0m);
        await sut.Invoking(service => service.GetMonthlySpendAsync(cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task MonthRollover_ResetsTrackedSpendBeforeReadsAndWrites()
    {
        var sut = CreateSut();
        await sut.RecordSpendAsync(25m, "operation");
        SetTrackingMonthToPreviousMonth(sut);

        (await sut.GetMonthlySpendAsync()).Should().Be(0m);
        await sut.RecordSpendAsync(10m, "operation");

        (await sut.GetMonthlySpendAsync()).Should().Be(10m);
    }

    [Fact]
    public void Constructor_WithNullDependencies_ThrowsArgumentNullException()
    {
        var options = Options.Create(new IngestionOptions());
        var logger = Mock.Of<ILogger<BudgetMonitorService>>();

        ((Action)(() => new BudgetMonitorService(null!, logger))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new BudgetMonitorService(options, null!))).Should().Throw<ArgumentNullException>();
    }

    private static BudgetMonitorService CreateSut(decimal monthlyLimit = 500m) => new(
        Options.Create(new IngestionOptions { MonthlyBudgetLimit = monthlyLimit }),
        Mock.Of<ILogger<BudgetMonitorService>>());

    private static void SetTrackingMonthToPreviousMonth(BudgetMonitorService sut)
    {
        var previous = DateTime.UtcNow.AddMonths(-1);
        var type = typeof(BudgetMonitorService);
        type.GetField("_trackingMonth", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(sut, previous.Month);
        type.GetField("_trackingYear", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(sut, previous.Year);
    }
}
