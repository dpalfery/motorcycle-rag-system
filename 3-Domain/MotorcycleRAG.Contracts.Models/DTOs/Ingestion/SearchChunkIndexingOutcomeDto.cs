using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.Contracts.Models.DTOs.Ingestion;

/// <summary>
/// The outcome of a search-chunk indexing operation performed by <see cref="ISearchChunkIndexingCoordinator"/>.
/// A property-bag carrier of result data with no domain invariant; conveys both success and failure details.
/// </summary>
public sealed record SearchChunkIndexingOutcomeDto(
    Guid IndexedArtifactId,
    Guid IngestionJobId,
    bool Succeeded,
    int ExpectedChunkCount,
    int IndexedChunkCount,
    int FailedChunkCount,
    IndexedArtifactState? ArtifactState,
    bool JobTransitionedToTerminal,
    string? FailureReason);
