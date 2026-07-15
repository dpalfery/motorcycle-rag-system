namespace MotorcycleRAG.Contracts.Models.DTOs.ManualIngestion;

public sealed class ManualRunDto
{
    public ManualRunDto() { }

    public ManualRunDto(Guid runId, Guid documentId, string runType, DateTimeOffset startedAtUtc,
        DateTimeOffset? completedAtUtc, ManualRunStatus status, string? startedFromStage,
        string? completedStage, string? localWorkingFolder, string? processorHost,
        string? errorSummary, int? chunkCount, int? graphEntityCount, int? graphRelationCount,
        int? vectorCount)
    {
        RunId = runId; DocumentId = documentId; RunType = runType; StartedAtUtc = startedAtUtc;
        CompletedAtUtc = completedAtUtc; Status = status; StartedFromStage = startedFromStage;
        CompletedStage = completedStage; LocalWorkingFolder = localWorkingFolder;
        ProcessorHost = processorHost; ErrorSummary = errorSummary; ChunkCount = chunkCount;
        GraphEntityCount = graphEntityCount; GraphRelationCount = graphRelationCount; VectorCount = vectorCount;
    }

    public Guid RunId { get; set; }
    public Guid DocumentId { get; set; }
    public string RunType { get; set; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public ManualRunStatus Status { get; set; }
    public string? StartedFromStage { get; set; }
    public string? CompletedStage { get; set; }
    public string? LocalWorkingFolder { get; set; }
    public string? ProcessorHost { get; set; }
    public string? ErrorSummary { get; set; }
    public int? ChunkCount { get; set; }
    public int? GraphEntityCount { get; set; }
    public int? GraphRelationCount { get; set; }
    public int? VectorCount { get; set; }
}
