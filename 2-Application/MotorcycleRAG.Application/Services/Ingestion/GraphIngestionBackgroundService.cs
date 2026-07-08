using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Entities;

namespace MotorcycleRAG.Application.Services.Ingestion;

/// <summary>
/// Background service that drains the bounded <see cref="GraphIngestionChannel"/> and executes
/// each graph ingestion job, replacing the previous fire-and-forget <c>Task.Run</c> pattern with
/// bounded backpressure and a single, durable consumer.
/// </summary>
/// <remarks>
/// <para>
/// This hosted service is registered as a singleton (the default for <c>AddHostedService</c>).
/// Because <see cref="IngestionJobService"/> (and its repository/storage dependencies) are scoped,
/// it must <b>not</b> be injected via the constructor — that would create a captive dependency
/// and throw under scope validation. Instead, the singleton <see cref="GraphIngestionChannel"/>
/// is injected directly (it is a singleton, so it is safe), and a fresh DI scope is created per
/// dequeued job to resolve <see cref="IIngestionJobService"/>. This mirrors
/// <see cref="JobDeletionBackgroundService"/> and follows the Microsoft guidance for consuming
/// scoped services inside a <see cref="BackgroundService"/>.
/// </para>
/// <para>
/// <see cref="GraphIngestionChannel"/> is configured with <c>SingleReader = true</c>; this service
/// is the only reader. Jobs are processed sequentially, which keeps graph write concurrency at one
/// (matching the Basic-tier graph store constraint).
/// </para>
/// </remarks>
public sealed class GraphIngestionBackgroundService : BackgroundService
{
    private readonly GraphIngestionChannel _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GraphIngestionBackgroundService> _logger;

    public GraphIngestionBackgroundService(
        GraphIngestionChannel channel,
        IServiceScopeFactory scopeFactory,
        ILogger<GraphIngestionBackgroundService> logger)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Graph ingestion background service started.");

        // ReadAllAsync completes when the channel writer completes (i.e. host shutdown signals
        // the writer). Until then it yields one job at a time, blocking without spinning while
        // the queue is empty.
        await foreach (var job in _channel.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            // A fresh scope per job ensures scoped repository/storage dependencies are resolved
            // correctly and are not held across the (potentially long) idle wait between jobs.
            using var scope = _scopeFactory.CreateScope();
            var jobService = scope.ServiceProvider.GetRequiredService<IIngestionJobService>();

            try
            {
                await jobService.ProcessGraphIngestionJobAsync(job).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Host is shutting down mid-job. Re-enqueue is intentionally NOT attempted: the
                // job row is still in the Processing status and will be observable via admin tooling.
                // Dropping it here is consistent with the prior Task.Run behaviour, which also
                // abandoned work on process exit.
                throw;
            }
            catch (Exception ex)
            {
                // ProcessGraphIngestionJobAsync captures ingestion failures and persists a Failed
                // status internally, so an exception escaping here is unexpected infrastructure
                // trouble. Never let the consumer loop die; log and continue draining the queue.
                _logger.LogError(
                    ex,
                    "Unexpected error processing graph ingestion job {JobId} for upload {UploadId}; job left in current status.",
                    job.IngestionJobId,
                    job.InputRef);
            }
        }

        _logger.LogInformation("Graph ingestion background service stopped.");
    }
}
