using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using MotorcycleRAG.Application.Services.Ingestion;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;
using Xunit;

namespace MotorcycleRAG.UnitTests.Pipeline;

/// <summary>
/// Unit tests for <see cref="JobDeletionBackgroundService"/>.
///
/// The service is a sealed <see cref="BackgroundService"/> subclass whose
/// <c>ExecuteAsync</c> loop is <c>protected</c>, so the tests invoke it through a
/// reflection helper (<see cref="InvokeExecuteAsync"/>) and let a short-lived
/// <see cref="CancellationTokenSource"/> terminate the loop. Each poll cycle runs
/// synchronously before the 10-second inter-cycle delay, so the per-job cleanup runs
/// well within the cancellation window.
/// </summary>
public class JobDeletionBackgroundServiceTests
{
    /// <summary>
    /// Builds the standard DI scope chain mocked by the background service:
    /// <c>IServiceScopeFactory</c> → <c>IServiceScope</c> → <c>IServiceProvider</c> that
    /// resolves both <see cref="IIngestionJobRepository"/> and <see cref="IIngestionJobService"/>.
    /// </summary>
    private static (Mock<IServiceScopeFactory> scopeFactory, Mock<IIngestionJobRepository> repo, Mock<IIngestionJobService> jobService)
        CreateMockScopeChain()
    {
        var repoMock = new Mock<IIngestionJobRepository>();
        var jobServiceMock = new Mock<IIngestionJobService>();

        // GetRequiredService<T>(sp) internally calls sp.GetService(typeof(T)).
        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock
            .Setup(p => p.GetService(typeof(IIngestionJobRepository)))
            .Returns(repoMock.Object);
        serviceProviderMock
            .Setup(p => p.GetService(typeof(IIngestionJobService)))
            .Returns(jobServiceMock.Object);

        var scopeMock = new Mock<IServiceScope>();
        scopeMock.SetupGet(s => s.ServiceProvider).Returns(serviceProviderMock.Object);

        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

        return (scopeFactoryMock, repoMock, jobServiceMock);
    }

    /// <summary>
    /// Invokes the protected <see cref="BackgroundService.ExecuteAsync"/> on the
    /// sealed service via reflection so tests can drive the poll loop directly with a
    /// short-lived cancellation token.
    /// </summary>
    private static async Task InvokeExecuteAsync(
        JobDeletionBackgroundService service,
        CancellationToken stoppingToken)
    {
        var method = typeof(JobDeletionBackgroundService)
            .GetMethod(
                "ExecuteAsync",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(CancellationToken) },
                modifiers: null)
            ?? throw new InvalidOperationException(
                "Could not resolve protected ExecuteAsync(CancellationToken) on JobDeletionBackgroundService.");

        var task = (Task)(method.Invoke(service, new object[] { stoppingToken })
            ?? throw new InvalidOperationException("ExecuteAsync returned null."));

        await task.ConfigureAwait(false);
    }

    [Fact]
    public async Task ExecuteAsync_WhenNoDeletingJobs_PerformsNoopCycle()
    {
        // Arrange: repository returns no Deleting jobs, so the cycle is a noop.
        var (scopeFactoryMock, repoMock, jobServiceMock) = CreateMockScopeChain();
        repoMock
            .Setup(r => r.GetByStatusesAsync(
                It.IsAny<IReadOnlyCollection<IngestionJobStatus>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<IngestionJob>());

        var loggerMock = new Mock<ILogger<JobDeletionBackgroundService>>();
        using var service = new JobDeletionBackgroundService(scopeFactoryMock.Object, loggerMock.Object);

        // Act: drive the loop until the short-lived token cancels during the 10s delay.
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await InvokeExecuteAsync(service, cts.Token);

        // Assert: the Deleting-status query ran, but no cleanup was dispatched.
        repoMock.Verify(
            r => r.GetByStatusesAsync(
                It.Is<IReadOnlyCollection<IngestionJobStatus>>(s => s.Contains(IngestionJobStatus.Deleting)),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
        jobServiceMock.Verify(
            s => s.ExecuteJobCleanupAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDeletingJobExists_CallsExecuteJobCleanupAsync()
    {
        // Arrange: one job in the Deleting state; cleanup completes successfully.
        var jobId = Guid.NewGuid();
        var (scopeFactoryMock, repoMock, jobServiceMock) = CreateMockScopeChain();
        repoMock
            .Setup(r => r.GetByStatusesAsync(
                It.IsAny<IReadOnlyCollection<IngestionJobStatus>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IngestionJob>
            {
                new() { IngestionJobId = jobId, Status = IngestionJobStatus.Deleting }
            });
        jobServiceMock
            .Setup(s => s.ExecuteJobCleanupAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var loggerMock = new Mock<ILogger<JobDeletionBackgroundService>>();
        using var service = new JobDeletionBackgroundService(scopeFactoryMock.Object, loggerMock.Object);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await InvokeExecuteAsync(service, cts.Token);

        // Assert: the cleanup was dispatched with the discovered job's identifier.
        repoMock.Verify(
            r => r.GetByStatusesAsync(
                It.Is<IReadOnlyCollection<IngestionJobStatus>>(s => s.Contains(IngestionJobStatus.Deleting)),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
        jobServiceMock.Verify(
            s => s.ExecuteJobCleanupAsync(jobId, It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_WhenExecuteJobCleanupAsyncThrows_LogsErrorAndContinues()
    {
        // Arrange: cleanup throws; the per-job exception must be caught inside CleanupJobAsync
        // and never propagate out of ExecuteAsync.
        var jobId = Guid.NewGuid();
        var (scopeFactoryMock, repoMock, jobServiceMock) = CreateMockScopeChain();
        repoMock
            .Setup(r => r.GetByStatusesAsync(
                It.IsAny<IReadOnlyCollection<IngestionJobStatus>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IngestionJob>
            {
                new() { IngestionJobId = jobId, Status = IngestionJobStatus.Deleting }
            });
        jobServiceMock
            .Setup(s => s.ExecuteJobCleanupAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("cleanup failed"));

        var loggerMock = new Mock<ILogger<JobDeletionBackgroundService>>();
        using var service = new JobDeletionBackgroundService(scopeFactoryMock.Object, loggerMock.Object);

        // Act: if the cleanup exception escaped, this await would throw and fail the test.
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await InvokeExecuteAsync(service, cts.Token);

        // Assert: cleanup was attempted and the failure was logged as an error.
        jobServiceMock.Verify(
            s => s.ExecuteJobCleanupAsync(jobId, It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);

        loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_WhenStoppingTokenAlreadyCancelled_ExitsCleanlyWithoutPolling()
    {
        // Arrange: the host token is already cancelled before the loop starts.
        // Equivalent to verifying the per-cycle / CleanupJobAsync stoppingToken guard:
        // an already-cancelled token must short-circuit the loop and never acquire the
        // semaphore or dispatch a cleanup.
        var (scopeFactoryMock, repoMock, jobServiceMock) = CreateMockScopeChain();

        var loggerMock = new Mock<ILogger<JobDeletionBackgroundService>>();
        using var service = new JobDeletionBackgroundService(scopeFactoryMock.Object, loggerMock.Object);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act: must return promptly without entering the poll loop.
        await InvokeExecuteAsync(service, cts.Token);

        // Assert: no repository query, no cleanup dispatch — clean exit on the stopping token.
        repoMock.Verify(
            r => r.GetByStatusesAsync(
                It.IsAny<IReadOnlyCollection<IngestionJobStatus>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        jobServiceMock.Verify(
            s => s.ExecuteJobCleanupAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void Constructor_WithNullArguments_ThrowsArgumentNullException()
    {
        var scopeFactory = new Mock<IServiceScopeFactory>();
        var logger = new Mock<ILogger<JobDeletionBackgroundService>>();

        var act1 = () => new JobDeletionBackgroundService(null!, logger.Object);
        act1.Should().Throw<ArgumentNullException>().WithParameterName("scopeFactory");

        var act2 = () => new JobDeletionBackgroundService(scopeFactory.Object, null!);
        act2.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task Dispose_DisposesSemaphore()
    {
        var (scopeFactory, _, _) = CreateMockScopeChain();
        var logger = new Mock<ILogger<JobDeletionBackgroundService>>();
        var service = new JobDeletionBackgroundService(scopeFactory.Object, logger.Object);

        service.Dispose();

        var cleanupMethod = typeof(JobDeletionBackgroundService).GetMethod(
            "CleanupJobAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        cleanupMethod.Should().NotBeNull();

        var act = async () =>
        {
            var task = (Task)cleanupMethod!.Invoke(service, [Guid.NewGuid(), TimeSpan.FromMinutes(5), CancellationToken.None])!;
            await task;
        };

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task CleanupJobAsync_WhenStoppingTokenCancelled_ThrowsOperationCanceledException()
    {
        var (scopeFactory, _, _) = CreateMockScopeChain();
        var logger = new Mock<ILogger<JobDeletionBackgroundService>>();
        var service = new JobDeletionBackgroundService(scopeFactory.Object, logger.Object);

        var cleanupMethod = typeof(JobDeletionBackgroundService).GetMethod(
            "CleanupJobAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        cleanupMethod.Should().NotBeNull();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () =>
        {
            var task = (Task)cleanupMethod!.Invoke(service, [Guid.NewGuid(), TimeSpan.FromMinutes(5), cts.Token])!;
            await task;
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task CleanupJobAsync_WhenCleanupTimesOut_LogsTimeoutErrorAndDoesNotThrow()
    {
        var jobId = Guid.NewGuid();
        var (scopeFactory, repoMock, jobServiceMock) = CreateMockScopeChain();
        var loggerMock = new Mock<ILogger<JobDeletionBackgroundService>>();

        jobServiceMock
            .Setup(s => s.ExecuteJobCleanupAsync(jobId, It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((_, token) =>
            {
                token.WaitHandle.WaitOne(1000);
                token.ThrowIfCancellationRequested();
            });

        var service = new JobDeletionBackgroundService(scopeFactory.Object, loggerMock.Object);

        var cleanupMethod = typeof(JobDeletionBackgroundService).GetMethod(
            "CleanupJobAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        cleanupMethod.Should().NotBeNull();

        var task = (Task)cleanupMethod!.Invoke(service, [jobId, TimeSpan.FromMilliseconds(10), CancellationToken.None])!;
        await task;

        loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("timed out")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenShutdownRequestedDuringLoop_SkipsRemainingJobs()
    {
        var job1 = new IngestionJob { IngestionJobId = Guid.NewGuid(), Status = IngestionJobStatus.Deleting };
        var job2 = new IngestionJob { IngestionJobId = Guid.NewGuid(), Status = IngestionJobStatus.Deleting };

        var (scopeFactory, repoMock, jobServiceMock) = CreateMockScopeChain();
        var loggerMock = new Mock<ILogger<JobDeletionBackgroundService>>();

        repoMock
            .Setup(r => r.GetByStatusesAsync(
                It.IsAny<IReadOnlyCollection<IngestionJobStatus>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IngestionJob> { job1, job2 });

        using var cts = new CancellationTokenSource();

        jobServiceMock
            .Setup(s => s.ExecuteJobCleanupAsync(job1.IngestionJobId, It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .Returns(Task.CompletedTask);

        var service = new JobDeletionBackgroundService(scopeFactory.Object, loggerMock.Object);

        await InvokeExecuteAsync(service, cts.Token);

        jobServiceMock.Verify(
            s => s.ExecuteJobCleanupAsync(job1.IngestionJobId, It.IsAny<CancellationToken>()),
            Times.Once);
        jobServiceMock.Verify(
            s => s.ExecuteJobCleanupAsync(job2.IngestionJobId, It.IsAny<CancellationToken>()),
            Times.Never);

        loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("skipping remaining")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenPollOnceAsyncThrowsException_LogsErrorAndContinuesLoop()
    {
        var (scopeFactory, repoMock, jobServiceMock) = CreateMockScopeChain();
        var loggerMock = new Mock<ILogger<JobDeletionBackgroundService>>();

        repoMock
            .SetupSequence(r => r.GetByStatusesAsync(
                It.IsAny<IReadOnlyCollection<IngestionJobStatus>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db error"))
            .ReturnsAsync(new List<IngestionJob>());

        var service = new JobDeletionBackgroundService(scopeFactory.Object, loggerMock.Object);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await InvokeExecuteAsync(service, cts.Token);

        loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Unexpected error")),
                It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }
}
