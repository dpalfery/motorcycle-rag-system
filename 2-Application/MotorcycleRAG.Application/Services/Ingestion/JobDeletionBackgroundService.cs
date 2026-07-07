using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.Application.Services.Ingestion;

/// <summary>
/// Background service that polls for ingestion jobs in the
/// <see cref="IngestionJobStatus.Deleting"/> state and performs best-effort cleanup of
/// their artifacts with an independent cancellation budget, decoupling long-running
/// deletions from the originating HTTP request lifecycle.
/// </summary>
/// <remarks>
/// <para>
/// This service owns the deletion execution loop. It does not perform the artifact
/// teardown itself; instead it resolves <see cref="IIngestionJobService"/> per job and
/// invokes <see cref="IIngestionJobService.ExecuteJobCleanupAsync"/>. The cleanup is
/// given its own <see cref="CancellationTokenSource"/> budget (5 minutes by default) so
/// that host shutdown or the poll cadence cannot truncate a deletion that is in flight.
/// </para>
/// <para>
/// Deletions are serialized through a <see cref="SemaphoreSlim"/> of size one to honour
/// the Basic-tier concurrency limit (only one deletion at a time). Failures are logged
/// and skipped; rolling the job status back to <see cref="IngestionJobStatus.Failed"/>
/// is the responsibility of <see cref="IIngestionJobService"/> (T8).
/// </para>
/// <para>
/// The poll interval and cleanup timeout are constants today to keep the constructor
/// surface minimal; promote them to an <c>IngestionOptions</c> section when runtime
/// configurability is required.
/// </para>
/// </remarks>
public sealed class JobDeletionBackgroundService : BackgroundService {
    /// <summary>
    /// Poll cadence for discovering <see cref="IngestionJobStatus.Deleting"/> jobs.
    /// Default: 10 seconds.
    /// </summary>
    private const int PollIntervalSeconds = 10;

    /// <summary>
    /// Per-job cleanup cancellation budget before the operation is abandoned.
    /// Default: 5 minutes. Deliberately independent of <c>stoppingToken</c>.
    /// </summary>
    private const int CleanupTimeoutMinutes = 5;

    private static readonly IReadOnlyCollection<IngestionJobStatus> DeletingStatuses =
        new[] { IngestionJobStatus.Deleting };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<JobDeletionBackgroundService> _logger;

    // Serializes deletions: only one concurrent cleanup is allowed (Basic-tier constraint).
    private readonly SemaphoreSlim _deletionSemaphore = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="JobDeletionBackgroundService"/> class.
    /// Scoped services are intentionally NOT injected; the singleton background service
    /// resolves them per cycle through <paramref name="scopeFactory"/>.
    /// </summary>
    /// <param name="scopeFactory">Factory used to create DI scopes per poll/cleanup.</param>
    /// <param name="logger">Logger for structured lifecycle and per-job diagnostics.</param>
    public JobDeletionBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<JobDeletionBackgroundService> logger) {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        var pollInterval = TimeSpan.FromSeconds(PollIntervalSeconds);
        var cleanupTimeout = TimeSpan.FromMinutes(CleanupTimeoutMinutes);

        _logger.LogInformation(
            "Job deletion background service started. Poll interval: {PollInterval}s, cleanup timeout: {CleanupTimeout}min",
            PollIntervalSeconds,
            CleanupTimeoutMinutes);

        while (!stoppingToken.IsCancellationRequested) {
            try {
                await PollOnceAsync(cleanupTimeout, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                // Host is shutting down mid-cycle; exit gracefully.
                break;
            }
            catch (Exception ex) {
                // Never let the poll loop die; log and continue to the next cycle.
                _logger.LogError(ex, "Unexpected error while polling for Deleting ingestion jobs");
            }

            if (stoppingToken.IsCancellationRequested) {
                break;
            }

            try {
                await Task.Delay(pollInterval, stoppingToken);
            }
            catch (OperationCanceledException) {
                // Shutdown signalled during the inter-cycle delay.
                break;
            }
        }

        _logger.LogInformation("Job deletion background service stopped");
    }

    /// <summary>
    /// Runs a single poll: discovers <see cref="IngestionJobStatus.Deleting"/> jobs and
    /// dispatches each to <see cref="CleanupJobAsync"/>.
    /// </summary>
    private async Task PollOnceAsync(TimeSpan cleanupTimeout, CancellationToken stoppingToken) {
        var deletingJobs = await QueryDeletingJobsAsync(stoppingToken);

        if (deletingJobs.Count == 0) {
            _logger.LogDebug(
                "No Deleting ingestion jobs found; sleeping for {PollInterval}s",
                PollIntervalSeconds);
            return;
        }

        _logger.LogInformation(
            "Found {Count} Deleting ingestion job(s) to clean up",
            deletingJobs.Count);

        // Index-based loop so we can report how many jobs were skipped on shutdown.
        for (var i = 0; i < deletingJobs.Count; i++) {
            if (stoppingToken.IsCancellationRequested) {
                // An in-flight cleanup (if any) is allowed to finish; do not start new ones.
                _logger.LogInformation(
                    "Shutdown requested; skipping remaining {Remaining} Deleting job(s)",
                    deletingJobs.Count - i);
                break;
            }

            // IngestionJobId is the public GUID identifier for the job.
            await CleanupJobAsync(deletingJobs[i].IngestionJobId, cleanupTimeout, stoppingToken);
        }
    }

    /// <summary>
    /// Resolves <see cref="IIngestionJobRepository"/> from a fresh scope and returns all
    /// jobs currently in the <see cref="IngestionJobStatus.Deleting"/> state.
    /// </summary>
    private async Task<IReadOnlyList<IngestionJob>> QueryDeletingJobsAsync(CancellationToken stoppingToken) {
        using var queryScope = _scopeFactory.CreateScope();
        var repository = queryScope.ServiceProvider.GetRequiredService<IIngestionJobRepository>();
        return await repository.GetByStatusesAsync(DeletingStatuses, stoppingToken);
    }

    /// <summary>
    /// Cleans up a single <see cref="IngestionJobStatus.Deleting"/> job under the
    /// deletion semaphore, with an independent cancellation budget that is NOT tied to
    /// the poll/host token. The semaphore acquisition honours <paramref name="stoppingToken"/>
    /// so that host shutdown is never blocked by a stuck cleanup holding the slot.
    /// </summary>
    /// <param name="jobId">The public GUID identifier of the job to clean up.</param>
    /// <param name="cleanupTimeout">The per-job cleanup budget (independent of <paramref name="stoppingToken"/>).</param>
    /// <param name="stoppingToken">
    /// The host shutdown token. Only applied to the semaphore wait; the in-flight cleanup
    /// itself runs under its own <paramref name="cleanupTimeout"/> budget.
    /// </param>
    private async Task CleanupJobAsync(Guid jobId, TimeSpan cleanupTimeout, CancellationToken stoppingToken) {
        // WaitAsync(stoppingToken) throws OperationCanceledException on shutdown BEFORE the
        // semaphore is acquired, so the finally below never runs Release() in that case —
        // Release() is only ever called when acquisition succeeded.
        await _deletionSemaphore.WaitAsync(stoppingToken).ConfigureAwait(false);
        try {
            _logger.LogInformation("Picking up Deleting ingestion job {JobId} for cleanup", jobId);

            using var cleanupCts = new CancellationTokenSource(cleanupTimeout);
            using var cleanupScope = _scopeFactory.CreateScope();
            var service = cleanupScope.ServiceProvider.GetRequiredService<IIngestionJobService>();

            try {
                await service.ExecuteJobCleanupAsync(jobId, cleanupCts.Token);
                _logger.LogInformation("Completed cleanup for ingestion job {JobId}", jobId);
            }
            catch (OperationCanceledException) when (cleanupCts.IsCancellationRequested) {
                // The 5-minute budget elapsed. The service (T8) is expected to roll the
                // job back to Failed; the background service must not crash.
                _logger.LogError(
                    "Cleanup for ingestion job {JobId} timed out after {Timeout}min",
                    jobId,
                    CleanupTimeoutMinutes);
            }
            catch (Exception ex) {
                // Status rollback is the service's responsibility (T8). Log and continue.
                _logger.LogError(ex, "Cleanup failed for ingestion job {JobId}", jobId);
            }
        }
        finally {
            _deletionSemaphore.Release();
        }
    }

    /// <inheritdoc/>
    public override void Dispose() {
        _deletionSemaphore.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
