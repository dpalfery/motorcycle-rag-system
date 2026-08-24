namespace MotorcycleRAG.Contracts.Models.DTOs.Ingestion;

/// <summary>
/// Result of a single orphan sweep cycle run by <see cref="IOrphanedArtifactSweepService.RunSweepAsync"/>.
/// A property-bag carrier that aggregates the outcomes of processing all Orphaned and OrphanedTerminal
/// blobs in the search-chunks container during one cycle.
/// </summary>
public sealed record OrphanSweepResultDto(
    int OrphansFound,
    int Healed,
    int AttemptsIncremented,
    int TerminalTransitions,
    int Errored);
