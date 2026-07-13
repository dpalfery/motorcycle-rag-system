using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Contracts.Models.DTOs.ManualIngestion;
using MotorcycleRAG.Domain.Constants;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace MotorcycleRAG.Application.Services.Ingestion;

public class ManualIngestionService : IManualIngestionService {
    private readonly IManualDocumentRepository _repository;
    private readonly IBlobStorageService _blobStorage;
    private readonly IIngestionJobService _ingestionJobService;
    private readonly IIngestionJobRepository _ingestionJobRepository;
    private readonly ILogger<ManualIngestionService> _logger;
    private const string ContainerName = "moto-manuals";

    public ManualIngestionService(
        IManualDocumentRepository repository,
        IBlobStorageService blobStorage,
        IIngestionJobService ingestionJobService,
        IIngestionJobRepository ingestionJobRepository,
        ILogger<ManualIngestionService> logger) {
        _repository = repository;
        _blobStorage = blobStorage;
        _ingestionJobService = ingestionJobService;
        _ingestionJobRepository = ingestionJobRepository;
        _logger = logger;
    }

    public async Task<ManualDocumentDto> RegisterManualAsync(RegisterManualDocumentRequest request, Stream fileStream, CancellationToken ct) {
        var documentId = Guid.NewGuid();
        var blobPath = $"{documentId}/{request.SourceFileName}";

        var blobUri = await _blobStorage.UploadAsync(
            ContainerName,
            blobPath,
            fileStream,
            "application/pdf",
            ct);

        var document = new ManualDocument {
            DocumentId = documentId,
            SourceFileName = request.SourceFileName,
            CanonicalBlobContainer = ContainerName,
            CanonicalBlobPath = blobPath,
            CanonicalBlobUri = blobUri,
            SourceContentHash = request.ContentHash,
            DocumentType = request.DocumentType,
            Make = request.Make,
            Model = request.Model,
            Year = request.Year,
            CurrentStatus = Domain.Enums.ManualDocumentStatus.Pending
        };

        var created = await _repository.CreateDocumentAsync(document, ct);
        return Map(created);
    }

    public async Task<ManualDocumentDto?> GetDocumentAsync(Guid documentId, CancellationToken ct) {
        var doc = await _repository.GetDocumentByIdAsync(documentId, ct);
        return doc != null ? Map(doc) : null;
    }

    public async Task<IEnumerable<ManualDocumentDto>> GetAllDocumentsAsync(CancellationToken ct) {
        var docs = await _repository.GetAllDocumentsAsync(ct);
        return docs.Select(Map);
    }

    public async Task<ManualRunDto> CreateRunAsync(CreateManualRunRequest request, CancellationToken ct) {
        var run = new ManualProcessingRun {
            RunId = Guid.NewGuid(),
            DocumentId = request.DocumentId,
            RunType = request.RunType,
            StartedFromStage = request.StartedFromStage,
            LocalWorkingFolder = request.LocalWorkingFolder,
            Status = Domain.Enums.ManualRunStatus.Started
        };

        var created = await _repository.CreateRunAsync(run, ct);

        // Update document status
        var doc = await _repository.GetDocumentByIdAsync(request.DocumentId, ct);
        if (doc != null) {
            doc.CurrentStatus = Domain.Enums.ManualDocumentStatus.Processing;
            doc.CurrentStage = request.StartedFromStage ?? ManualIngestionStages.Source;
            await _repository.UpdateDocumentAsync(doc, ct);
        }

        return Map(created);
    }

    public async Task<ManualRunDto?> GetRunAsync(Guid runId, CancellationToken ct) {
        var run = await _repository.GetRunByIdAsync(runId, ct);
        return run != null ? Map(run) : null;
    }

    public async Task<IEnumerable<ManualRunDto>> GetRunsForDocumentAsync(Guid documentId, CancellationToken ct) {
        var runs = await _repository.GetRunsForDocumentAsync(documentId, ct);
        return runs.Select(Map);
    }

    public async Task<IEnumerable<ManualStageDto>> GetStagesForRunAsync(Guid runId, CancellationToken ct) {
        var stages = await _repository.GetStagesForRunAsync(runId, ct);
        return stages.Select(Map);
    }

    public async Task ReportStageStartAsync(Guid runId, string stageName, ManualStageStartRequest request, CancellationToken ct) {
        var stage = new ManualProcessingStage {
            StageId = Guid.NewGuid(),
            RunId = runId,
            StageName = stageName,
            Status = Domain.Enums.ManualStageStatus.Started,
            MetadataJson = request.MetadataJson
        };

        await _repository.CreateStageAsync(stage, ct);

        // Update run and document current stage
        var run = await _repository.GetRunByIdAsync(runId, ct);
        if (run != null) {
            run.Status = Domain.Enums.ManualRunStatus.InProgress;
            await _repository.UpdateRunAsync(run, ct);

            var doc = await _repository.GetDocumentByIdAsync(run.DocumentId, ct);
            if (doc != null) {
                doc.CurrentStage = stageName;
                await _repository.UpdateDocumentAsync(doc, ct);
            }
        }
    }

    public async Task ReportStageCompleteAsync(Guid runId, string stageName, ManualStageCompleteRequest request, CancellationToken ct) {
        var stages = await _repository.GetStagesForRunAsync(runId, ct);
        var stage = stages.FirstOrDefault(s => s.StageName == stageName && s.Status == Domain.Enums.ManualStageStatus.Started);

        if (stage != null) {
            stage.Status = Domain.Enums.ManualStageStatus.Completed;
            stage.CompletedAtUtc = DateTimeOffset.UtcNow;
            stage.ArtifactPath = request.ArtifactPath;
            stage.ArtifactHash = request.ArtifactHash;
            stage.MetadataJson = request.MetadataJson;
            await _repository.UpdateStageAsync(stage, ct);
        }

        if (stageName == ManualIngestionStages.Complete) {
            var run = await _repository.GetRunByIdAsync(runId, ct);
            if (run != null) {
                run.Status = Domain.Enums.ManualRunStatus.Succeeded;
                run.CompletedAtUtc = DateTimeOffset.UtcNow;
                run.CompletedStage = stageName;
                await _repository.UpdateRunAsync(run, ct);

                var doc = await _repository.GetDocumentByIdAsync(run.DocumentId, ct);
                if (doc != null) {
                    doc.CurrentStatus = Domain.Enums.ManualDocumentStatus.Processed;
                    doc.LastSuccessfulRunId = runId;
                    doc.LastProcessedAtUtc = DateTimeOffset.UtcNow;
                    await _repository.UpdateDocumentAsync(doc, ct);
                }
            }
        }
    }

    public async Task ReportStageFailAsync(Guid runId, string stageName, ManualStageFailRequest request, CancellationToken ct) {
        var stages = await _repository.GetStagesForRunAsync(runId, ct);
        var stage = stages.FirstOrDefault(s => s.StageName == stageName && s.Status == Domain.Enums.ManualStageStatus.Started);

        if (stage != null) {
            stage.Status = Domain.Enums.ManualStageStatus.Failed;
            stage.CompletedAtUtc = DateTimeOffset.UtcNow;
            stage.ErrorDetail = request.ErrorDetail;
            stage.MetadataJson = request.MetadataJson;
            await _repository.UpdateStageAsync(stage, ct);
        }

        var run = await _repository.GetRunByIdAsync(runId, ct);
        if (run != null) {
            run.Status = Domain.Enums.ManualRunStatus.Failed;
            run.CompletedAtUtc = DateTimeOffset.UtcNow;
            run.ErrorSummary = request.ErrorDetail;
            await _repository.UpdateRunAsync(run, ct);

            var doc = await _repository.GetDocumentByIdAsync(run.DocumentId, ct);
            if (doc != null) {
                doc.CurrentStatus = Domain.Enums.ManualDocumentStatus.Failed;
                doc.LastFailure = request.ErrorDetail;
                await _repository.UpdateDocumentAsync(doc, ct);
            }
        }
    }

    public async Task RegisterArtifactAsync(Guid runId, ManualArtifactRegistrationRequest request, CancellationToken ct) {
        var run = await _repository.GetRunByIdAsync(runId, ct);
        if (run != null) {
            // Update metrics based on artifact registration
            if (request.ArtifactType == ManualIngestionArtifactTypes.Chunks) run.ChunkCount = request.ItemCount;
            if (request.ArtifactType == ManualIngestionArtifactTypes.Entities) run.GraphEntityCount = request.ItemCount;
            if (request.ArtifactType == ManualIngestionArtifactTypes.Relationships) run.GraphRelationCount = request.ItemCount;
            if (request.ArtifactType == ManualIngestionArtifactTypes.Vectors) run.VectorCount = request.ItemCount;

            await _repository.UpdateRunAsync(run, ct);
        }
    }

    public async Task<IngestionJobStatusResponse> CreateGraphSeedJobAsync(CreateGraphSeedJobRequest request, string userId, CancellationToken ct) {
        var jobRequest = new IngestionJobStartRequest {
            UploadId = request.FileName, // Assuming FileName is used as UploadId for now or mapped
            DocumentType = "bike-graph",
            ProcessorRunId = Guid.NewGuid().ToString("N")
        };

        return await _ingestionJobService.StartJobAsync(jobRequest, userId, ct);
    }

    public async Task<IEnumerable<UnifiedOperationDto>> GetOperationsAsync(CancellationToken ct) {
        var operations = new List<UnifiedOperationDto>();

        // Fix N+1 problem: Use JOIN query
        var manualOps = await _repository.GetRecentManualOperationsAsync(50, ct);
        foreach (var op in manualOps) {
            operations.Add(new UnifiedOperationDto(
                op.Run.RunId.ToString(),
                "ManualIngestion",
                op.Run.Status.ToString(),
                op.Run.StartedAtUtc,
                op.Run.CompletedAtUtc,
                op.Document.SourceFileName,
                op.Run.CompletedStage ?? op.Document.CurrentStage,
                op.Run.ErrorSummary
            ));
        }

        // Add IngestionJobs (for Graph Seeding)
        var recentJobs = await _ingestionJobRepository.GetRecentAsync(50, ct);
        foreach (var job in recentJobs) {
            operations.Add(new UnifiedOperationDto(
                job.IngestionJobId.ToString(),
                job.InputType == IngestionJobType.BikeGraph ? "GraphSeeding" : "LegacyIngestion",
                job.Status.ToString(),
                job.CreatedAtUtc,
                job.CompletedAtUtc,
                job.SourceFileName ?? job.InputRef,
                null,
                job.FailureReason
            ));
        }

        return operations.OrderByDescending(o => o.StartedAtUtc);
    }

    private ManualDocumentDto Map(ManualDocument doc) {
        return new ManualDocumentDto(
            doc.DocumentId,
            doc.SourceFileName,
            doc.CanonicalBlobPath,
            CreateCanonicalBlobUri(doc.CanonicalBlobUri),
            doc.SourceContentHash,
            doc.DocumentType,
            doc.Make,
            doc.Model,
            doc.Year,
            doc.UploadedAtUtc,
            (Contracts.Models.DTOs.ManualIngestion.ManualDocumentStatus)doc.CurrentStatus,
            doc.CurrentStage,
            doc.LastSuccessfulRunId,
            doc.LastFailure
        );
    }

    private static Uri? CreateCanonicalBlobUri(string? canonicalBlobUri) {
        return Uri.TryCreate(canonicalBlobUri, UriKind.Absolute, out var uri) ? uri : null;
    }

    private ManualRunDto Map(ManualProcessingRun run) {
        return new ManualRunDto(
            run.RunId,
            run.DocumentId,
            run.RunType,
            run.StartedAtUtc,
            run.CompletedAtUtc,
            (Contracts.Models.DTOs.ManualIngestion.ManualRunStatus)run.Status,
            run.StartedFromStage,
            run.CompletedStage,
            run.LocalWorkingFolder,
            run.ProcessorHost,
            run.ErrorSummary,
            run.ChunkCount,
            run.GraphEntityCount,
            run.GraphRelationCount,
            run.VectorCount
        );
    }

    private ManualStageDto Map(ManualProcessingStage stage) {
        return new ManualStageDto(
            stage.StageId,
            stage.RunId,
            stage.StageName,
            (Contracts.Models.DTOs.ManualIngestion.ManualStageStatus)stage.Status,
            stage.StartedAtUtc,
            stage.CompletedAtUtc,
            stage.ArtifactPath,
            stage.ArtifactHash,
            stage.MetadataJson,
            stage.ErrorDetail
        );
    }
}
