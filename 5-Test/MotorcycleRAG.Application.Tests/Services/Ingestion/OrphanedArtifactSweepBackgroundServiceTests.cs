using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Application.Services.Ingestion;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs.Ingestion;
using MotorcycleRAG.Core.Options;
using Xunit;

namespace MotorcycleRAG.UnitTests.Services.Ingestion;

/// <summary>
/// RED-phase tests for the not-yet-implemented <see cref="OrphanedArtifactSweepBackgroundService"/>
/// (plan 2026-08-03-processor-artifact-skip-observability §4 T15).
///
/// <see cref="OrphanedArtifactSweepBackgroundService"/> is expected to follow the
/// <c>JobDeletionBackgroundService</c> shape (see
/// <c>5-Test/MotorcycleRAG.Application.Tests/Pipeline/JobDeletionBackgroundServiceTests.cs</c>):
/// a sealed <see cref="BackgroundService"/> subclass whose <c>ExecuteAsync</c> loop is
/// <c>protected</c>, so these tests invoke it through a reflection helper
/// (<see cref="InvokeExecuteAsync"/>) and let a short-lived <see cref="CancellationTokenSource"/>
/// terminate the loop, rather than driving the real poll cadence through
/// <c>StartAsync</c>/<c>Task.Delay</c> (slow and flaky).
/// </summary>
public class OrphanedArtifactSweepBackgroundServiceTests
{
    private static readonly OrphanSweepResultDto EmptySweepResult = new(0, 0, 0, 0, 0);

    /// <summary>
    /// Builds a scope factory whose <c>CreateScope()</c> returns a BRAND NEW
    /// <see cref="IServiceScope"/> mock (with its own <see cref="IServiceProvider"/> mock) on
    /// every invocation, mirroring what a real <see cref="IServiceScopeFactory"/> does. All
    /// scopes resolve the same <see cref="IOrphanedArtifactSweepService"/> mock instance (the
    /// scope itself, not the resolved service, is what must be fresh per cycle — see (b)).
    /// Every created scope mock is captured in <c>createdScopes</c> so tests can assert on
    /// scope-creation count and per-scope disposal.
    /// </summary>
    private static (Mock<IServiceScopeFactory> ScopeFactory, Mock<IOrphanedArtifactSweepService> SweepService, List<Mock<IServiceScope>> CreatedScopes)
        CreateMockScopeChain()
    {
        var sweepServiceMock = new Mock<IOrphanedArtifactSweepService>();
        sweepServiceMock
            .Setup(s => s.RunSweepAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptySweepResult);

        var createdScopes = new List<Mock<IServiceScope>>();

        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        scopeFactoryMock
            .Setup(f => f.CreateScope())
            .Returns(() =>
            {
                var serviceProviderMock = new Mock<IServiceProvider>();
                serviceProviderMock
                    .Setup(p => p.GetService(typeof(IOrphanedArtifactSweepService)))
                    .Returns(sweepServiceMock.Object);

                var scopeMock = new Mock<IServiceScope>();
                scopeMock.SetupGet(s => s.ServiceProvider).Returns(serviceProviderMock.Object);

                createdScopes.Add(scopeMock);
                return scopeMock.Object;
            });

        return (scopeFactoryMock, sweepServiceMock, createdScopes);
    }

    private static IOptions<IngestionOptions> CreateOptions(TimeSpan sweepInterval) =>
        Options.Create(new IngestionOptions { OrphanSweepInterval = sweepInterval });

    /// <summary>
    /// Invokes the protected <see cref="BackgroundService.ExecuteAsync"/> on the sealed
    /// service via reflection so tests can drive the sweep loop directly with a short-lived
    /// cancellation token, per the <c>JobDeletionBackgroundServiceTests</c> precedent.
    /// </summary>
    private static async Task InvokeExecuteAsync(
        OrphanedArtifactSweepBackgroundService service,
        CancellationToken stoppingToken)
    {
        var method = typeof(OrphanedArtifactSweepBackgroundService)
            .GetMethod(
                "ExecuteAsync",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(CancellationToken) },
                modifiers: null)
            ?? throw new InvalidOperationException(
                "Could not resolve protected ExecuteAsync(CancellationToken) on OrphanedArtifactSweepBackgroundService.");

        var task = (Task)(method.Invoke(service, new object[] { stoppingToken })
            ?? throw new InvalidOperationException("ExecuteAsync returned null."));

        await task.ConfigureAwait(false);
    }

    // ---------------------------------------------------------------------
    // (a) A single cycle calls RunSweepAsync on a service resolved from a scope.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task ExecuteAsync_WhenCycleRuns_ResolvesSweepServiceFromScopeAndCallsRunSweepAsync()
    {
        // Arrange: a long interval means only the first (synchronous) cycle can complete
        // before the short-lived token cancels the loop during the inter-cycle delay.
        var (scopeFactoryMock, sweepServiceMock, _) = CreateMockScopeChain();
        var loggerMock = new Mock<ILogger<OrphanedArtifactSweepBackgroundService>>();
        var options = CreateOptions(TimeSpan.FromSeconds(5));

        using var service = new OrphanedArtifactSweepBackgroundService(scopeFactoryMock.Object, options, loggerMock.Object);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await InvokeExecuteAsync(service, cts.Token);

        // Assert: exactly one scope was created and RunSweepAsync was invoked on the service
        // resolved from it.
        scopeFactoryMock.Verify(f => f.CreateScope(), Times.Once);
        sweepServiceMock.Verify(s => s.RunSweepAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---------------------------------------------------------------------
    // (b) Multiple cycles each resolve a NEW scope, not reusing one across cycles.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task ExecuteAsync_WhenMultipleCyclesRun_ResolvesFreshScopeEachCycleAndDisposesIt()
    {
        // Arrange: a short interval within the observation window forces several cycles.
        var (scopeFactoryMock, sweepServiceMock, createdScopes) = CreateMockScopeChain();
        var loggerMock = new Mock<ILogger<OrphanedArtifactSweepBackgroundService>>();
        var options = CreateOptions(TimeSpan.FromMilliseconds(20));

        using var service = new OrphanedArtifactSweepBackgroundService(scopeFactoryMock.Object, options, loggerMock.Object);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        await InvokeExecuteAsync(service, cts.Token);

        // Assert: CreateScope() was called once PER CYCLE (not once for the service's whole
        // lifetime) and every scope it produced was disposed before the next cycle began -
        // proof no scoped dependency (e.g. a DB context) is held across cycles.
        scopeFactoryMock.Verify(f => f.CreateScope(), Times.AtLeast(2));
        createdScopes.Count.Should().BeGreaterThanOrEqualTo(2);
        sweepServiceMock.Verify(s => s.RunSweepAsync(It.IsAny<CancellationToken>()), Times.AtLeast(2));

        foreach (var scopeMock in createdScopes)
        {
            scopeMock.Verify(s => s.Dispose(), Times.Once);
        }
    }

    // ---------------------------------------------------------------------
    // (c) A throwing cycle is caught, logged, and the loop continues.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task ExecuteAsync_WhenRunSweepAsyncThrows_LogsErrorAndContinuesToNextCycle()
    {
        // Arrange: the first cycle throws; the second (and any subsequent) succeeds.
        var (scopeFactoryMock, sweepServiceMock, _) = CreateMockScopeChain();
        sweepServiceMock
            .SetupSequence(s => s.RunSweepAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("sweep failed"))
            .ReturnsAsync(EmptySweepResult);

        var loggerMock = new Mock<ILogger<OrphanedArtifactSweepBackgroundService>>();
        var options = CreateOptions(TimeSpan.FromMilliseconds(20));

        using var service = new OrphanedArtifactSweepBackgroundService(scopeFactoryMock.Object, options, loggerMock.Object);

        // Act: if the sweep exception escaped ExecuteAsync, this await would throw and the
        // test would fail here rather than at an explicit assertion.
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await InvokeExecuteAsync(service, cts.Token);

        // Assert: the loop survived the throw and ran at least one more cycle afterwards.
        sweepServiceMock.Verify(s => s.RunSweepAsync(It.IsAny<CancellationToken>()), Times.AtLeast(2));

        loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    // ---------------------------------------------------------------------
    // (d) Cancellation exits gracefully - no unhandled OperationCanceledException.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task ExecuteAsync_WhenStoppingTokenAlreadyCancelled_ExitsCleanlyWithoutRunningCycle()
    {
        // Arrange: the host token is already cancelled before the loop starts.
        var (scopeFactoryMock, sweepServiceMock, _) = CreateMockScopeChain();
        var loggerMock = new Mock<ILogger<OrphanedArtifactSweepBackgroundService>>();
        var options = CreateOptions(TimeSpan.FromSeconds(5));

        using var service = new OrphanedArtifactSweepBackgroundService(scopeFactoryMock.Object, options, loggerMock.Object);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act: must return promptly without entering the sweep loop or throwing.
        var act = async () => await InvokeExecuteAsync(service, cts.Token);
        await act.Should().NotThrowAsync();

        // Assert: no scope was created, no sweep was dispatched.
        scopeFactoryMock.Verify(f => f.CreateScope(), Times.Never);
        sweepServiceMock.Verify(s => s.RunSweepAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelledDuringInterCycleDelay_ExitsGracefullyWithoutThrowing()
    {
        // Arrange: a long interval guarantees the token cancels DURING the Task.Delay
        // between cycles, not before or during a subsequent cycle.
        var (scopeFactoryMock, sweepServiceMock, _) = CreateMockScopeChain();
        var loggerMock = new Mock<ILogger<OrphanedArtifactSweepBackgroundService>>();
        var options = CreateOptions(TimeSpan.FromSeconds(10));

        using var service = new OrphanedArtifactSweepBackgroundService(scopeFactoryMock.Object, options, loggerMock.Object);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Act: no unhandled OperationCanceledException should escape.
        var act = async () => await InvokeExecuteAsync(service, cts.Token);
        await act.Should().NotThrowAsync();

        // Assert: exactly the one cycle that ran before the delay began.
        sweepServiceMock.Verify(s => s.RunSweepAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelledBetweenCycles_StopsBeforeStartingNextCycle()
    {
        // Arrange: cancellation is triggered from inside the sweep call itself (simulating a
        // host shutdown signalled while a cycle is in flight) with a very short interval, so
        // if the loop failed to observe the token it would very likely start another cycle
        // before the test could assert.
        var (scopeFactoryMock, sweepServiceMock, _) = CreateMockScopeChain();
        var loggerMock = new Mock<ILogger<OrphanedArtifactSweepBackgroundService>>();
        var options = CreateOptions(TimeSpan.FromMilliseconds(10));

        using var cts = new CancellationTokenSource();

        sweepServiceMock
            .Setup(s => s.RunSweepAsync(It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .ReturnsAsync(EmptySweepResult);

        using var service = new OrphanedArtifactSweepBackgroundService(scopeFactoryMock.Object, options, loggerMock.Object);

        // Act
        var act = async () => await InvokeExecuteAsync(service, cts.Token);
        await act.Should().NotThrowAsync();

        // Assert: the loop exited before dispatching a second cycle.
        sweepServiceMock.Verify(s => s.RunSweepAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---------------------------------------------------------------------
    // (e) The delay between cycles uses the configured OrphanSweepInterval.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task ExecuteAsync_UsesConfiguredOrphanSweepIntervalBetweenCycles()
    {
        // Arrange: a 30ms interval observed over a 200ms window must yield several cycles.
        // IngestionOptions.OrphanSweepInterval defaults to 5 minutes, so if ExecuteAsync used
        // that default (or any other hardcoded interval on the order of seconds) instead of
        // the configured value, at most one cycle would be observed in this window.
        var (scopeFactoryMock, sweepServiceMock, _) = CreateMockScopeChain();
        var loggerMock = new Mock<ILogger<OrphanedArtifactSweepBackgroundService>>();
        var options = CreateOptions(TimeSpan.FromMilliseconds(30));

        using var service = new OrphanedArtifactSweepBackgroundService(scopeFactoryMock.Object, options, loggerMock.Object);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await InvokeExecuteAsync(service, cts.Token);

        // Assert: bounded above and below to avoid CI timing flakiness while still pinning
        // that the cadence is driven by the configured (short) interval.
        sweepServiceMock.Verify(s => s.RunSweepAsync(It.IsAny<CancellationToken>()), Times.AtLeast(3));
        sweepServiceMock.Verify(s => s.RunSweepAsync(It.IsAny<CancellationToken>()), Times.AtMost(50));
    }

    // ---------------------------------------------------------------------
    // Bonus: constructor guard clauses, matching the JobDeletionBackgroundService precedent.
    // ---------------------------------------------------------------------
    [Fact]
    public void Constructor_WithNullArguments_ThrowsArgumentNullException()
    {
        var scopeFactory = new Mock<IServiceScopeFactory>();
        var options = CreateOptions(TimeSpan.FromMinutes(5));
        var logger = new Mock<ILogger<OrphanedArtifactSweepBackgroundService>>();

        var act1 = () => new OrphanedArtifactSweepBackgroundService(null!, options, logger.Object);
        act1.Should().Throw<ArgumentNullException>().WithParameterName("scopeFactory");

        var act2 = () => new OrphanedArtifactSweepBackgroundService(scopeFactory.Object, null!, logger.Object);
        act2.Should().Throw<ArgumentNullException>().WithParameterName("ingestionOptions");

        var act3 = () => new OrphanedArtifactSweepBackgroundService(scopeFactory.Object, options, null!);
        act3.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }
}
