using MotorcycleRAG.Contracts.Models.DTOs.ManualIngestion;
using MotorcycleRAG.Domain.Entities;

namespace MotorcycleRAG.Contracts.Interfaces;

public interface IManualDocumentRepository
{
    Task<ManualDocument> CreateDocumentAsync(ManualDocument document, CancellationToken cancellationToken = default);
    Task<ManualDocument?> GetDocumentByIdAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task<IEnumerable<ManualDocument>> GetAllDocumentsAsync(CancellationToken cancellationToken = default);
    Task UpdateDocumentAsync(ManualDocument document, CancellationToken cancellationToken = default);
    
    Task<ManualRunDto> CreateRunAsync(ManualRunDto run, CancellationToken cancellationToken = default);
    Task<ManualRunDto?> GetRunByIdAsync(Guid runId, CancellationToken cancellationToken = default);
    Task<IEnumerable<ManualRunDto>> GetRunsForDocumentAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task UpdateRunAsync(ManualRunDto run, CancellationToken cancellationToken = default);

    Task<ManualStageDto> CreateStageAsync(ManualStageDto stage, CancellationToken cancellationToken = default);
    Task<IEnumerable<ManualStageDto>> GetStagesForRunAsync(Guid runId, CancellationToken cancellationToken = default);
    Task UpdateStageAsync(ManualStageDto stage, CancellationToken cancellationToken = default);

    Task<IEnumerable<(ManualDocument Document, ManualRunDto Run)>> GetRecentManualOperationsAsync(int top, CancellationToken cancellationToken = default);
}
