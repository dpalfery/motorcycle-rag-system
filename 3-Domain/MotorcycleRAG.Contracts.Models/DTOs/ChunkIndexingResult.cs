namespace MotorcycleRAG.Contracts.Models.DTOs;

public record ChunkIndexingResult(int TotalParsed, IReadOnlyList<ChunkIndexOutcome> Outcomes);

public record ChunkIndexOutcome(
    string ChunkId,
    bool Succeeded,
    string? FailureReason,
    int PageNumber,
    int ChunkIndex,
    string? SourceFile);
