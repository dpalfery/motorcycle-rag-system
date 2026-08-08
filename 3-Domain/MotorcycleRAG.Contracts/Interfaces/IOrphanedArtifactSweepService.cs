using MotorcycleRAG.Contracts.Models.DTOs.Ingestion;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Service that reconciles orphaned search-chunks artifacts (blobs whose ingestion jobs are missing or failed).
/// Orchestrates the healing and terminal-state transitions of artifacts in the search-chunks container,
/// implementing the sweep algorithm defined in plan 2026-08-03-processor-artifact-skip-observability §3 Steps 3-4.
/// </summary>
public interface IOrphanedArtifactSweepService
{
    /// <summary>
    /// Lists all orphaned search-chunks artifacts in both Orphaned and OrphanedTerminal states.
    /// Used by the admin UI to display which artifacts are currently orphaned and their metadata.
    /// </summary>
    /// <param name="cancellationToken">Propagates the caller's cancellation.</param>
    /// <returns>
    /// A read-only collection of orphaned artifacts, including their state, reason, attempt count,
    /// and first-detection timestamp.
    /// </returns>
    Task<IReadOnlyList<OrphanedArtifactDto>> ListOrphansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs one reconciliation cycle over the search-chunks container's Orphaned and OrphanedTerminal blobs.
    /// Heals blobs whose ingestion jobs are now resolvable, increments attempt counts for blobs still absent,
    /// and transitions blobs to terminal state when they exceed the retry budget or retention window.
    /// Never throws: every per-blob failure (job-resolution exception, metadata-write exception) is caught,
    /// counted in the returned DTO's Errored total, and does not abort the remaining blobs in the cycle.
    /// </summary>
    /// <param name="cancellationToken">Propagates the caller's cancellation.</param>
    /// <returns>
    /// An aggregated result DTO containing counts of blobs found, healed, attempt-incremented, transitioned
    /// to terminal, and encountered errors during this cycle.
    /// </returns>
    Task<OrphanSweepResultDto> RunSweepAsync(CancellationToken cancellationToken = default);
}
