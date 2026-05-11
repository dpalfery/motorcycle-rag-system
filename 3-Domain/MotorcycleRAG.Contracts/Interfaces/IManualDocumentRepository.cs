using MotorcycleRAG.Domain.Entities;

namespace MotorcycleRAG.Contracts.Interfaces;

public interface IManualDocumentRepository
{
    Task<ManualDocument> CreateDocumentAsync(ManualDocument document, CancellationToken cancellationToken = default);
    Task<ManualDocument?> GetDocumentByIdAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task<IEnumerable<ManualDocument>> GetAllDocumentsAsync(CancellationToken cancellationToken = default);
    Task UpdateDocumentAsync(ManualDocument document, CancellationToken cancellationToken = default);
    
    Task<ManualProcessingRun> CreateRunAsync(ManualProcessingRun run, CancellationToken cancellationToken = default);
    Task<ManualProcessingRun?> GetRunByIdAsync(Guid runId, CancellationToken cancellationToken = default);
    Task<IEnumerable<ManualProcessingRun>> GetRunsForDocumentAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task UpdateRunAsync(ManualProcessingRun run, CancellationToken cancellationToken = default);

    Task<ManualProcessingStage> CreateStageAsync(ManualProcessingStage stage, CancellationToken cancellationToken = default);
    Task<IEnumerable<ManualProcessingStage>> GetStagesForRunAsync(Guid runId, CancellationToken cancellationToken = default);
    Task UpdateStageAsync(ManualProcessingStage stage, CancellationToken cancellationToken = default);

    Task<IEnumerable<(ManualDocument Document, ManualProcessingRun Run)>> GetRecentManualOperationsAsync(int top, CancellationToken cancellationToken = default);
}
