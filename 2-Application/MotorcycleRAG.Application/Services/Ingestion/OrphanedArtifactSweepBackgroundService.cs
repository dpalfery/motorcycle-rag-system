using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.Application.Services.Ingestion;

/// <summary>
/// Background service that periodically sweeps for orphaned search-chunks artifacts
/// (blobs whose ingestion jobs are missing or failed) and reconciles their states.
/// Runs the orphan reconciliation loop at a configurable interval, resolving scoped
/// dependencies fresh on each cycle.
/// </summary>
/// <remarks>
/// <para>
/// This service owns the orphan sweep execution loop. It does not perform the
/// reconciliation itself; instead it resolves <see cref="IOrphanedArtifactSweepService"/>
/// per cycle and invokes <see cref="IOrphanedArtifactSweepService.RunSweepAsync"/>.
/// </para>
/// <para>
/// The sweep interval is configured via <see cref="IngestionOptions.OrphanSweepInterval"/>
/// (defaults to 5 minutes). Failures are logged and skipped; rolling the artifact status
/// back to a terminal state is the responsibility of <see cref="IOrphanedArtifactSweepService"/>.
/// </para>
/// <para>
/// Sweeps are serialized through a <see cref="SemaphoreSlim"/> of size one to protect
/// against concurrent execution when both the periodic loop and a future on-demand
/// <c>POST /api/ingestion/artifacts/orphaned/sweep</c> endpoint invoke
/// <see cref="IOrphanedArtifactSweepService.RunSweepAsync"/> simultaneously over the
/// same blob container.
/// </para>
/// </remarks>
public sealed class OrphanedArtifactSweepBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<IngestionOptions> _ingestionOptions;
    private readonly ILogger<OrphanedArtifactSweepBackgroundService> _logger;
    private readonly TimeProvider _timeProvider;

    // Serializes sweeps: only one concurrent sweep is allowed to protect blob container
    // operations. Acquired by the periodic loop; released by future on-demand endpoint.
    private readonly SemaphoreSlim _sweepSemaphore = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="OrphanedArtifactSweepBackgroundService"/> class.
    /// Scoped services are intentionally NOT injected; the singleton background service
    /// resolves them per cycle through <paramref name="scopeFactory"/>.
    /// </summary>
    /// <param name="scopeFactory">Factory used to create DI scopes per sweep cycle.</param>
    /// <param name="ingestionOptions">Configuration for the ingestion pipeline, including sweep interval.</param>
    /// <param name="logger">Logger for structured lifecycle and per-cycle diagnostics.</param>
    public OrphanedArtifactSweepBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<IngestionOptions> ingestionOptions,
        ILogger<OrphanedArtifactSweepBackgroundService> logger)
        : this(scopeFactory, ingestionOptions, logger, TimeProvider.System)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OrphanedArtifactSweepBackgroundService"/> class
    /// with an explicit <see cref="TimeProvider"/>, used to make the inter-cycle delay
    /// deterministically controllable in tests. Production resolves the overload above
    /// (without the <c>TimeProvider</c> parameter), which delegates to this constructor with <see cref="TimeProvider.System"/>.
    /// </summary>
    /// <param name="scopeFactory">Factory used to create DI scopes per sweep cycle.</param>
    /// <param name="ingestionOptions">Configuration for the ingestion pipeline, including sweep interval.</param>
    /// <param name="logger">Logger for structured lifecycle and per-cycle diagnostics.</param>
    /// <param name="timeProvider">Time provider that drives the inter-cycle delay. Production uses <see cref="TimeProvider.System"/>.</param>
    public OrphanedArtifactSweepBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<IngestionOptions> ingestionOptions,
        ILogger<OrphanedArtifactSweepBackgroundService> logger,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(ingestionOptions);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _scopeFactory = scopeFactory;
        _ingestionOptions = ingestionOptions;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sweepInterval = _ingestionOptions.Value.OrphanSweepInterval;

        _logger.LogInformation(
            "Orphaned artifact sweep background service started. Sweep interval: {SweepInterval}ms",
            sweepInterval.TotalMilliseconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // WaitAsync(stoppingToken) throws OperationCanceledException on shutdown BEFORE the
                // semaphore is acquired, so the finally below never runs Release() in that case —
                // Release() is only ever called when acquisition succeeded.
                await _sweepSemaphore.WaitAsync(stoppingToken).ConfigureAwait(false);
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var sweepService = scope.ServiceProvider.GetRequiredService<IOrphanedArtifactSweepService>();
                    await sweepService.RunSweepAsync(stoppingToken).ConfigureAwait(false);
                }
                finally
                {
                    _sweepSemaphore.Release();
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Host is shutting down mid-cycle; exit gracefully.
                break;
            }
            catch (Exception ex)
            {
                // Never let the sweep loop die; log and continue to the next cycle.
                _logger.LogError(ex, "Unexpected error while sweeping orphaned artifacts");
            }

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await Task.Delay(sweepInterval, _timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Shutdown signalled during the inter-cycle delay.
                break;
            }
        }

        _logger.LogInformation("Orphaned artifact sweep background service stopped");
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        _sweepSemaphore.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
