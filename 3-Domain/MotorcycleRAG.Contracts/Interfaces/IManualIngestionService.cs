using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Contracts.Models.DTOs.ManualIngestion;

namespace MotorcycleRAG.Contracts.Interfaces;

public interface IManualIngestionService
{
    Task<ManualDocumentDto> RegisterManualAsync(RegisterManualDocumentRequest request, Stream fileStream, CancellationToken ct);
    Task<ManualDocumentDto?> GetDocumentAsync(Guid documentId, CancellationToken ct);
    Task<IEnumerable<ManualDocumentDto>> GetAllDocumentsAsync(CancellationToken ct);
    
    Task<ManualRunDto> CreateRunAsync(CreateManualRunRequest request, CancellationToken ct);
    Task<ManualRunDto?> GetRunAsync(Guid runId, CancellationToken ct);
    Task<IEnumerable<ManualRunDto>> GetRunsForDocumentAsync(Guid documentId, CancellationToken ct);
    Task<IEnumerable<ManualStageDto>> GetStagesForRunAsync(Guid runId, CancellationToken ct);
    
    Task ReportStageStartAsync(Guid runId, string stageName, ManualStageStartRequest request, CancellationToken ct);
    Task ReportStageCompleteAsync(Guid runId, string stageName, ManualStageCompleteRequest request, CancellationToken ct);
    Task ReportStageFailAsync(Guid runId, string stageName, ManualStageFailRequest request, CancellationToken ct);
    
    Task RegisterArtifactAsync(Guid runId, ManualArtifactRegistrationRequest request, CancellationToken ct);
    
    Task<IngestionJobStatusResponse> CreateGraphSeedJobAsync(CreateGraphSeedJobRequest request, string userId, CancellationToken ct);
    
    Task<IEnumerable<UnifiedOperationDto>> GetOperationsAsync(CancellationToken ct);
}
