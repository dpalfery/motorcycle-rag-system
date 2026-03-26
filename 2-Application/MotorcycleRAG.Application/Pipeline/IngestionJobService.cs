using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.Application.Pipeline;

/// <summary>
/// Orchestrates the ingestion job lifecycle: creation, status retrieval, and cancellation.
/// Delegates persistence to <see cref="IIngestionJobRepository"/> and runtime execution
/// to the configured <see cref="ILocalPipelineService"/> implementation.
/// </summary>
public sealed class IngestionJobService : IIngestionJobService {
    private const string PdfSourceFileName = "source.pdf";
    private const string GraphEntitiesPrefix = "graph-entities";

    private readonly IIngestionJobRepository _repository;
    private readonly IBlobStorageService _blobStorageService;
    private readonly BlobStorageOptions _blobStorageOptions;
    private readonly ILocalPipelineService _pipelineService;
    private readonly IGraphEntityIngestionService _graphEntityIngestionService;
    private readonly IngestionOptions _options;
    private readonly ILogger<IngestionJobService> _logger;

    public IngestionJobService(
        IIngestionJobRepository repository,
        IBlobStorageService blobStorageService,
        ILocalPipelineService pipelineService,
        IGraphEntityIngestionService graphEntityIngestionService,
        IOptions<BlobStorageOptions> blobStorageOptions,
        IOptions<IngestionOptions> options,
        ILogger<IngestionJobService> logger) {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _blobStorageService = blobStorageService ?? throw new ArgumentNullException(nameof(blobStorageService));
        _pipelineService = pipelineService ?? throw new ArgumentNullException(nameof(pipelineService));
        _graphEntityIngestionService = graphEntityIngestionService ?? throw new ArgumentNullException(nameof(graphEntityIngestionService));
        _blobStorageOptions = blobStorageOptions?.Value ?? throw new ArgumentNullException(nameof(blobStorageOptions));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PendingStorageFileDto>> GetPendingStorageFilesAsync(
        CancellationToken ct = default) {
        var sourceBlobs = await _blobStorageService.ListAsync(_blobStorageOptions.RawUploadsContainer, ct)
            .ConfigureAwait(false);

        var candidates = new Dictionary<string, PendingStorageFileDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var blob in sourceBlobs) {
            if (!TryCreatePendingStorageFile(blob, out var pendingFile)) {
                continue;
            }

            var dedupeKey = $"{pendingFile.DocumentType}:{pendingFile.UploadId}";
            if (!candidates.TryGetValue(dedupeKey, out var existing)
                || pendingFile.LastModifiedUtc > existing.LastModifiedUtc) {
                candidates[dedupeKey] = pendingFile;
            }
        }

        var pendingFiles = new List<PendingStorageFileDto>(candidates.Count);
        foreach (var candidate in candidates.Values) {
            var primaryJobType = GetPrimaryInputType(candidate.DocumentType);
            var latestJob = await _repository.GetLatestByInputAsync(candidate.UploadId, primaryJobType, ct).ConfigureAwait(false);
            IngestionJob? latestGraphJob = null;
            if (SupportsGraphImport(candidate.DocumentType)) {
                latestGraphJob = await _repository.GetLatestByInputAsync(candidate.UploadId, IngestionJobType.BikeGraph, ct).ConfigureAwait(false);
            }

            if (!ShouldIncludeAsPending(candidate.DocumentType, latestJob, latestGraphJob)) {
                continue;
            }

            pendingFiles.Add(candidate with {
                LastKnownJobStatus = latestJob?.Status.ToString(),
                FailureReason = latestJob?.FailureReason,
                GraphImportStatus = latestGraphJob?.Status.ToString(),
                GraphImportFailureReason = latestGraphJob?.FailureReason
            });
        }

        return pendingFiles
            .OrderByDescending(file => file.LastModifiedUtc)
            .ThenBy(file => file.BlobName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IngestionJobStatusResponse>> GetRecentIngestionJobsAsync(
        int maxCount = 50,
        CancellationToken ct = default) {
        if (maxCount <= 0) {
            throw new ArgumentOutOfRangeException(nameof(maxCount), "maxCount must be greater than zero.");
        }

        var jobs = await _repository.GetRecentAsync(maxCount, ct).ConfigureAwait(false);
        var refreshedJobs = new List<IngestionJob>(jobs.Count);
        foreach (var job in jobs) {
            refreshedJobs.Add(await RefreshJobStatusAsync(job, ct).ConfigureAwait(false));
        }

        return refreshedJobs.Select(MapToResponse).ToArray();
    }

    /// <inheritdoc />
    public async Task<IngestionJobStatusResponse> StartJobAsync(
        IngestionJobStartRequest request,
        string userId,
        CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var inputType = MapDocumentType(request.DocumentType);
        var pipelineId = GetPipelineId(inputType);

        var job = new IngestionJob {
            InputType = inputType,
            InputRef = request.UploadId,
            CreatedBySubject = userId,
            Status = IngestionJobStatus.Queued,
            ComputeProvider = _options.Mode == ProcessingMode.Local ? "LocalProcessingService" : "MicrosoftFabric"
        };

        job = await _repository.CreateAsync(job, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Ingestion job {JobId} created for input type {InputType}.",
            job.IngestionJobId,
            job.InputType);

        string runId;
        try {
            runId = await _pipelineService.TriggerPipelineAsync(
                request.UploadId,
                request.DocumentType,
                pipelineId,
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) {
            _logger.LogError(
                ex,
                "Failed to trigger pipeline for job {JobId}.",
                job.IngestionJobId);

            await _repository.UpdateStatusAsync(
                job.IngestionJobId,
                IngestionJobStatus.Failed,
                "Failed to trigger pipeline.",
                ct).ConfigureAwait(false);

            job.Status = IngestionJobStatus.Failed;
            job.FailureReason = "Failed to trigger pipeline.";
            return MapToResponse(job);
        }

        job.FabricRunId = runId;
        job.Status = IngestionJobStatus.Processing;
        job.StartedAtUtc = DateTimeOffset.UtcNow;

        await _repository.UpdateAsync(job, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Pipeline triggered for job {JobId} with run {RunId} (mode: {Mode}).",
            job.IngestionJobId,
            runId,
            _options.Mode);

        return MapToResponse(job);
    }

    /// <inheritdoc />
    public async Task<IngestionJobStatusResponse?> GetJobStatusAsync(
        Guid jobId,
        string userId,
        CancellationToken ct = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var job = await _repository.GetByIdAsync(jobId, ct).ConfigureAwait(false);
        if (job is null)
            return null;

        job = await RefreshJobStatusAsync(job, ct).ConfigureAwait(false);
        return MapToResponse(job);
    }

    /// <inheritdoc />
    public async Task CancelJobAsync(
        Guid jobId,
        string userId,
        CancellationToken ct = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var job = await _repository.GetByIdAsync(jobId, ct).ConfigureAwait(false);
        if (job is null)
            throw new InvalidOperationException($"Ingestion job '{jobId}' not found.");

        if (job.Status is IngestionJobStatus.Completed
            or IngestionJobStatus.Failed
            or IngestionJobStatus.Cancelled
            or IngestionJobStatus.PartiallyCompleted) {
            _logger.LogWarning(
                "Cannot cancel job {JobId} in terminal status {Status}.",
                jobId,
                job.Status);
            return;
        }

        await _repository.UpdateStatusAsync(
            jobId,
            IngestionJobStatus.Cancelled,
            "Cancelled by user.",
            ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Ingestion job {JobId} cancelled.",
            jobId);
    }

    /// <summary>Maps a domain <see cref="IngestionJob"/> to its response DTO.</summary>
    private IngestionJobStatusResponse MapToResponse(IngestionJob job) {
        IReadOnlyList<int> missingPages = [];
        if (!string.IsNullOrEmpty(job.MissingPagesJson)) {
            try {
                missingPages = JsonSerializer.Deserialize<int[]>(job.MissingPagesJson) ?? [];
            }
            catch (JsonException) {
                // Malformed JSON — return empty list rather than throwing.
            }
        }

        return new IngestionJobStatusResponse {
            JobId = job.IngestionJobId,
            Status = job.Status.ToString(),
            CreatedAtUtc = job.CreatedAtUtc,
            StartedAtUtc = job.StartedAtUtc,
            CompletedAtUtc = job.CompletedAtUtc,
            InputType = job.InputType.ToString(),
            InputRef = job.InputRef,
            ManualDocumentId = job.ManualDocumentId,
            TotalPages = job.TotalPages,
            PagesCapturedViewableCount = job.PagesCapturedViewableCount,
            PagesWithSearchableTextCount = job.PagesWithSearchableTextCount,
            PagesWithOcrTextCount = job.PagesWithOcrTextCount,
            PagesWithNativeTextCount = job.PagesWithNativeTextCount,
            MissingPages = missingPages,
            Coverage = CoverageCalculator.Calculate(job),
            WorkloadLimits = new IngestionWorkloadLimits {
                MaxPages = job.TotalPages ?? 0,
                MaxInputBytes = _options.MaxInputBytes,
                MaxRuntimeMinutes = _options.PipelineTimeoutMinutes
            },
            FailureReason = job.FailureReason,
            FabricRunId = job.FabricRunId
        };
    }

    private async Task<IngestionJob> RefreshJobStatusAsync(IngestionJob job, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(job);

        if (!CanRefreshStatus(job)) {
            return job;
        }

        string externalStatus;
        try {
            externalStatus = await _pipelineService.GetRunStatusAsync(
                job.FabricRunId!,
                GetPipelineId(job.InputType),
                ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) {
            _logger.LogWarning(ex, "Failed to refresh ingestion job {JobId} from the pipeline service.", job.IngestionJobId);
            return job;
        }

        var refreshedStatus = MapPipelineStatus(externalStatus, job.Status);

        if (job.InputType == IngestionJobType.BikeGraph && refreshedStatus == IngestionJobStatus.Completed) {
            try {
                await EnsureGraphArtifactsExistAsync(job.InputRef, ct).ConfigureAwait(false);
                await _graphEntityIngestionService.IngestAsync(job.InputRef, ct).ConfigureAwait(false);
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Graph import failed for ingestion job {JobId}.", job.IngestionJobId);
                job.Status = IngestionJobStatus.Failed;
                job.FailureReason = "Graph import failed after local bike graph processing completed.";
                job.CompletedAtUtc ??= DateTimeOffset.UtcNow;
                await _repository.UpdateAsync(job, ct).ConfigureAwait(false);
                return job;
            }
        }

        if (job.Status == refreshedStatus && (!IsTerminalStatus(refreshedStatus) || job.CompletedAtUtc.HasValue)) {
            return job;
        }

        job.Status = refreshedStatus;
        if (IsTerminalStatus(refreshedStatus)) {
            job.CompletedAtUtc ??= DateTimeOffset.UtcNow;
        }
        else {
            job.CompletedAtUtc = null;
        }

        if (refreshedStatus == IngestionJobStatus.Failed && string.IsNullOrWhiteSpace(job.FailureReason)) {
            job.FailureReason = $"Pipeline reported status '{externalStatus}'.";
        }
        else if (refreshedStatus is not IngestionJobStatus.Failed and not IngestionJobStatus.Cancelled) {
            job.FailureReason = null;
        }

        await _repository.UpdateAsync(job, ct).ConfigureAwait(false);
        return job;
    }

    private async Task EnsureGraphArtifactsExistAsync(string uploadId, CancellationToken ct) {
        var blobPath = $"{GraphEntitiesPrefix}/{uploadId}/entities.json";
        var exists = await _blobStorageService.ExistsAsync(_blobStorageOptions.RawUploadsContainer, blobPath, ct).ConfigureAwait(false);
        if (!exists) {
            throw new InvalidOperationException($"Expected graph entities blob '{blobPath}' was not found.");
        }
    }

    private bool CanRefreshStatus(IngestionJob job) =>
        !string.IsNullOrWhiteSpace(job.FabricRunId) && !IsTerminalStatus(job.Status);

    private IngestionJobType MapDocumentType(string documentType) => documentType switch {
        "manual-pdf" => IngestionJobType.PDFManual,
        "spec-dataset" => IngestionJobType.StructuredSpecification,
        "bike-graph" => IngestionJobType.BikeGraph,
        _ => throw new ArgumentException($"Unsupported document type: '{documentType}'.", nameof(documentType))
    };

    private string GetPipelineId(IngestionJobType inputType) => inputType switch {
        IngestionJobType.PDFManual => _options.PdfPipelineId,
        IngestionJobType.StructuredSpecification => _options.CsvPipelineId,
        IngestionJobType.BikeGraph => string.Empty,
        _ => throw new InvalidOperationException($"No pipeline configured for input type '{inputType}'.")
    };

    private static IngestionJobType GetPrimaryInputType(string documentType) => documentType switch {
        "manual-pdf" => IngestionJobType.PDFManual,
        "spec-dataset" => IngestionJobType.StructuredSpecification,
        _ => throw new ArgumentException($"Unsupported pending document type '{documentType}'.", nameof(documentType))
    };

    private static IngestionJobStatus MapPipelineStatus(string externalStatus, IngestionJobStatus fallbackStatus) {
        if (string.IsNullOrWhiteSpace(externalStatus)) {
            return fallbackStatus;
        }

        return externalStatus.Trim() switch {
            var status when status.Equals("queued", StringComparison.OrdinalIgnoreCase) => IngestionJobStatus.Queued,
            var status when status.Equals("processing", StringComparison.OrdinalIgnoreCase) => IngestionJobStatus.Processing,
            var status when status.Equals("running", StringComparison.OrdinalIgnoreCase) => IngestionJobStatus.Processing,
            var status when status.Equals("inprogress", StringComparison.OrdinalIgnoreCase) => IngestionJobStatus.Processing,
            var status when status.Equals("in_progress", StringComparison.OrdinalIgnoreCase) => IngestionJobStatus.Processing,
            var status when status.Equals("indexing", StringComparison.OrdinalIgnoreCase) => IngestionJobStatus.Indexing,
            var status when status.Equals("completed", StringComparison.OrdinalIgnoreCase) => IngestionJobStatus.Completed,
            var status when status.Equals("succeeded", StringComparison.OrdinalIgnoreCase) => IngestionJobStatus.Completed,
            var status when status.Equals("success", StringComparison.OrdinalIgnoreCase) => IngestionJobStatus.Completed,
            var status when status.Equals("failed", StringComparison.OrdinalIgnoreCase) => IngestionJobStatus.Failed,
            var status when status.Equals("error", StringComparison.OrdinalIgnoreCase) => IngestionJobStatus.Failed,
            var status when status.Equals("cancelled", StringComparison.OrdinalIgnoreCase) => IngestionJobStatus.Cancelled,
            var status when status.Equals("canceled", StringComparison.OrdinalIgnoreCase) => IngestionJobStatus.Cancelled,
            var status when status.Equals("partiallycompleted", StringComparison.OrdinalIgnoreCase) => IngestionJobStatus.PartiallyCompleted,
            var status when status.Equals("partially_completed", StringComparison.OrdinalIgnoreCase) => IngestionJobStatus.PartiallyCompleted,
            _ => fallbackStatus
        };
    }

    private static bool IsTerminalStatus(IngestionJobStatus status) =>
        status is IngestionJobStatus.Completed
            or IngestionJobStatus.Failed
            or IngestionJobStatus.Cancelled
            or IngestionJobStatus.PartiallyCompleted;

    private static bool ShouldIncludeAsPending(
        string documentType,
        IngestionJob? primaryJob,
        IngestionJob? graphJob) {
        if (SupportsGraphImport(documentType)) {
            return HasPendingWorkflow(primaryJob) || HasPendingWorkflow(graphJob);
        }

        return HasPendingWorkflow(primaryJob);
    }

    private static bool HasPendingWorkflow(IngestionJob? job) {
        return job is null
               || job.Status is IngestionJobStatus.Failed
               or IngestionJobStatus.Cancelled;
    }

    private static bool SupportsGraphImport(string documentType) =>
        string.Equals(documentType, "spec-dataset", StringComparison.OrdinalIgnoreCase);

    private static bool TryCreatePendingStorageFile(
        BlobObjectDescriptor blob,
        out PendingStorageFileDto pendingFile) {
        pendingFile = default!;

        if (string.IsNullOrWhiteSpace(blob.Name) || !blob.LastModifiedUtc.HasValue) {
            return false;
        }

        var normalizedBlobName = blob.Name.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalizedBlobName)) {
            return false;
        }

        var segments = normalizedBlobName.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 2
            && string.Equals(segments[1], PdfSourceFileName, StringComparison.OrdinalIgnoreCase)) {
            pendingFile = new PendingStorageFileDto {
                UploadId = segments[0],
                BlobName = normalizedBlobName,
                DocumentType = "manual-pdf",
                SizeBytes = blob.SizeBytes,
                LastModifiedUtc = blob.LastModifiedUtc.Value
            };
            return true;
        }

        if (segments.Length == 1 && normalizedBlobName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) {
            var uploadId = Path.GetFileNameWithoutExtension(normalizedBlobName);
            if (string.IsNullOrWhiteSpace(uploadId)) {
                return false;
            }

            pendingFile = new PendingStorageFileDto {
                UploadId = uploadId,
                BlobName = normalizedBlobName,
                DocumentType = "spec-dataset",
                SizeBytes = blob.SizeBytes,
                LastModifiedUtc = blob.LastModifiedUtc.Value
            };
            return true;
        }

        return false;
    }
}
