using System.Text.Json;
using Azure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Contracts.Repositories;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Core.Utilities;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.Application.Services.Ingestion;

/// <summary>
/// Orchestrates the ingestion job lifecycle: creation, status retrieval, and cancellation.
/// Delegates persistence to <see cref="IIngestionJobRepository"/>; local processor execution
/// is initiated by Admin Desktop and reported through processor callback endpoints.
/// </summary>
public sealed class IngestionJobService : IIngestionJobService {
    private const string PdfSourceFileName = "source.pdf";
    private const string GraphEntitiesPrefix = "graph-entities";
    private static readonly IngestionJobStatus[] ActiveStatuses = [IngestionJobStatus.Processing, IngestionJobStatus.Indexing];
    private static readonly IngestionJobStatus[] FailedStatuses = [IngestionJobStatus.Failed, IngestionJobStatus.Cancelled];
    private static readonly IngestionJobStatus[] FinishedStatuses = [IngestionJobStatus.Completed, IngestionJobStatus.PartiallyCompleted];

    private readonly IIngestionJobRepository _repository;
    private readonly IBlobStorageService _blobStorageService;
    private readonly IIndexedArtifactRepository _artifactRepository;
    private readonly IIndexedChunkRepository _chunkRepository;
    private readonly IAzureSearchDocumentService _searchDocumentService;
    private readonly IGraphRepository _graphRepository;
    private readonly BlobStorageOptions _blobStorageOptions;
    private readonly IGraphEntityIngestionService _graphEntityIngestionService;
    private readonly IngestionOptions _options;
    private readonly ILogger<IngestionJobService> _logger;

    public IngestionJobService(
        IIngestionJobRepository repository,
        IBlobStorageService blobStorageService,
        IIndexedArtifactRepository artifactRepository,
        IIndexedChunkRepository chunkRepository,
        IAzureSearchDocumentService searchDocumentService,
        IGraphRepository graphRepository,
        IGraphEntityIngestionService graphEntityIngestionService,
        IOptions<BlobStorageOptions> blobStorageOptions,
        IOptions<IngestionOptions> options,
        ILogger<IngestionJobService> logger) {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _blobStorageService = blobStorageService ?? throw new ArgumentNullException(nameof(blobStorageService));
        _artifactRepository = artifactRepository ?? throw new ArgumentNullException(nameof(artifactRepository));
        _chunkRepository = chunkRepository ?? throw new ArgumentNullException(nameof(chunkRepository));
        _searchDocumentService = searchDocumentService ?? throw new ArgumentNullException(nameof(searchDocumentService));
        _graphRepository = graphRepository ?? throw new ArgumentNullException(nameof(graphRepository));
        _graphEntityIngestionService = graphEntityIngestionService ?? throw new ArgumentNullException(nameof(graphEntityIngestionService));
        _blobStorageOptions = blobStorageOptions?.Value ?? throw new ArgumentNullException(nameof(blobStorageOptions));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PendingStorageFileDto>> GetPendingStorageFilesAsync(
        CancellationToken ct = default) {
        IReadOnlyList<BlobObjectDescriptor> sourceBlobs;
        try {
            sourceBlobs = await _blobStorageService.ListAsync(_blobStorageOptions.RawUploadsContainer, ct)
                .ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == 403) {
            _logger.LogWarning(
                ex,
                "Listing pending storage files was denied for container {Container}. Returning an empty result set.",
                LogSanitizer.Sanitize(_blobStorageOptions.RawUploadsContainer, 80));
            return Array.Empty<PendingStorageFileDto>();
        }

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
    public async Task DeletePendingStorageFileAsync(
        string uploadId,
        string documentType,
        CancellationToken ct = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(uploadId);

        await _repository.DeleteByInputRefAsync(uploadId, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Deleted pending ingestion upload {UploadId} for document type {DocumentType}.",
            LogSanitizer.Sanitize(uploadId),
            LogSanitizer.Sanitize(documentType));
    }

    /// <inheritdoc />
    public async Task<int> ClearPendingStorageFilesAsync(
        CancellationToken ct = default) {
        var pendingFiles = await GetPendingStorageFilesAsync(ct).ConfigureAwait(false);
        if (pendingFiles.Count == 0) {
            return 0;
        }

        var deletedCount = 0;
        var processedUploadIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pendingFile in pendingFiles) {
            if (!processedUploadIds.Add(pendingFile.UploadId)) {
                continue;
            }

            await DeletePendingStorageFileAsync(pendingFile.UploadId, pendingFile.DocumentType, ct).ConfigureAwait(false);
            deletedCount++;
        }

        return deletedCount;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IngestionJobStatusResponse>> GetRecentIngestionJobsAsync(
        int maxCount = 50,
        CancellationToken ct = default) {
        if (maxCount <= 0) {
            throw new ArgumentOutOfRangeException(nameof(maxCount), "maxCount must be greater than zero.");
        }

        var jobs = await _repository.GetRecentAsync(maxCount, ct).ConfigureAwait(false);
        return jobs.Select(MapToResponse).ToArray();
    }

    /// <inheritdoc />
    public async Task DeleteJobAsync(
        Guid jobId,
        string userId,
        CancellationToken ct = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var job = await _repository.GetByIdAsync(jobId, ct).ConfigureAwait(false);
        if (job is null) {
            throw new DeleteJobException(
                DeleteJobError.NotFound,
                $"Ingestion job '{jobId}' not found.");
        }

        if (ActiveStatuses.Contains(job.Status)) {
            throw new DeleteJobException(
                DeleteJobError.Active,
                $"Ingestion job '{jobId}' is active and cannot be deleted.");
        }

        // Idempotency guard: a job already marked for deletion must not be re-queued.
        // The background cleanup service (ExecuteJobCleanupAsync) owns the asset teardown
        // and final row delete, so this method only performs the status transition and
        // returns immediately, keeping the HTTP delete path well under request timeout.
        if (job.Status == IngestionJobStatus.Deleting) {
            throw new DeleteJobException(
                DeleteJobError.AlreadyDeleting,
                $"Ingestion job '{jobId}' is already being deleted.");
        }

        // Atomically transition the job to Deleting. The conditional UPDATE rejects
        // concurrent delete requests: only one can win the WHERE [Status] IN (...) match,
        // eliminating the read-then-write race where two requests both pass the guards above
        // and both flip the same terminal job to Deleting. CancellationToken.None is
        // intentional: this critical transition must complete even when the HTTP request is
        // approaching its timeout, so the background service reliably observes Deleting.
        var succeeded = await _repository.TrySetDeletingAsync(jobId, CancellationToken.None).ConfigureAwait(false);
        if (!succeeded) {
            throw new DeleteJobException(
                DeleteJobError.ConcurrentModification,
                "The job could not be deleted. It may have been modified concurrently.");
        }

        _logger.LogInformation(
            "Ingestion job {JobId} marked for deletion (status: Deleting).",
            jobId);
    }

    /// <inheritdoc />
    public async Task ExecuteJobCleanupAsync(Guid jobId, CancellationToken ct = default) {
        // Driven by JobDeletionBackgroundService with its own cancellation budget; not an HTTP path.
        var job = await _repository.GetByIdAsync(jobId, ct).ConfigureAwait(false);
        if (job is null) {
            // Already removed by a prior cleanup pass — treat as success.
            _logger.LogWarning(
                "Background cleanup: IngestionJob with ID {JobId} not found; may have been deleted by a prior cleanup run",
                jobId);
            return;
        }

        // Best-effort cleanup of artifacts, chunks, blobs, search documents, and graph rows.
        // The whole teardown + final row delete is wrapped so that ANY unhandled exception
        // (e.g. from GetDeleteArtifactsAsync/GetDeleteChunksAsync, which are NOT covered by
        // RunBestEffortAsync) rolls the job back to Failed instead of leaving it stuck in
        // Deleting. CancellationToken.None is intentional for the rollback write: the caller's
        // token may be the very one that triggered this catch via cancellation.
        try {
            await DeleteAssociatedAssetsAsync(job, ct).ConfigureAwait(false);

            var deleted = await _repository.DeleteAsync(job.IngestionJobId, ct).ConfigureAwait(false);
            if (deleted) {
                _logger.LogInformation(
                    "Background cleanup: Successfully deleted IngestionJob {JobId} and all associated assets",
                    jobId);
            }
            else {
                // DeleteAsync returned false — the row was already gone (e.g. a prior cleanup
                // pass or a concurrent operator action). Not an error; log and consider cleanup done.
                _logger.LogWarning(
                    "Background cleanup: IngestionJob {JobId} row was not present during final delete; may have been removed by a prior run",
                    jobId);
            }
        }
        catch (Exception ex) {
            // Asset teardown or final delete failed (or the caller's cleanup budget elapsed).
            // Roll the job back to Failed so it resurfaces for operator attention.
            job.Status = IngestionJobStatus.Failed;
            job.FailureReason = $"Background cleanup failed: {ex.Message}";
            await _repository.UpdateAsync(job, CancellationToken.None).ConfigureAwait(false);
            _logger.LogError(
                ex,
                "Background cleanup: Failed to cleanup IngestionJob {JobId}; rolled back to Failed status",
                jobId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<int> ClearFailedJobsAsync(
        string userId,
        CancellationToken ct = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return await _repository.DeleteByStatusesAsync(FailedStatuses, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> ClearFinishedJobsAsync(
        string userId,
        CancellationToken ct = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var finishedJobs = await _repository.GetByStatusesAsync(FinishedStatuses, ct).ConfigureAwait(false);
        if (finishedJobs.Count == 0) {
            return 0;
        }

        var finishedJobIds = finishedJobs
            .Select(static job => job.IngestionJobId)
            .Distinct()
            .ToArray();

        return await _repository.DeleteByIdsAsync(finishedJobIds, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IngestionJobStatusResponse> RetryJobAsync(
        Guid jobId,
        string userId,
        CancellationToken ct = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var job = await _repository.GetByIdAsync(jobId, ct).ConfigureAwait(false);
        if (job is null) {
            throw new InvalidOperationException($"Ingestion job '{jobId}' not found.");
        }

        if (!IsFailedStatus(job.Status)) {
            throw new InvalidOperationException("Only failed or cancelled ingestion jobs can be retried.");
        }

        throw new InvalidOperationException(
            "Local-first ingestion retry requires re-queueing the original local source file from Admin Desktop.");
    }

    /// <inheritdoc />
    public async Task<IngestionJobStatusResponse> StartJobAsync(
        IngestionJobStartRequest request,
        string userId,
        CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProcessorRunId);

        var job = new IngestionJob {
            InputType = MapDocumentType(request.DocumentType),
            InputRef = request.UploadId,
            CreatedBySubject = userId,
            Status = IngestionJobStatus.Queued,
            StartedAtUtc = null,
            ComputeProvider = "AdminLocalProcessor",
            DocIngestionRunId = request.ProcessorRunId
        };

        job = await _repository.CreateAsync(job, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Ingestion job {JobId} created for input type {InputType}.",
            job.IngestionJobId,
            job.InputType);

        _logger.LogInformation(
            "Queued ingestion job {JobId} for processor run {ProcessorRunId}.",
            job.IngestionJobId,
            LogSanitizer.Sanitize(request.ProcessorRunId));

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

        return MapToResponse(job);
    }

    /// <inheritdoc />
    public async Task<IngestionJobStatusResponse> ImportGraphArtifactsAsync(
        GraphImportStartRequest request,
        string userId,
        CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UploadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var job = new IngestionJob {
            InputType = IngestionJobType.BikeGraph,
            InputRef = request.UploadId,
            CreatedBySubject = userId,
            Status = IngestionJobStatus.Processing,
            StartedAtUtc = DateTimeOffset.UtcNow,
            ComputeProvider = "AdminLocalProcessor"
        };

        job = await _repository.CreateAsync(job, ct).ConfigureAwait(false);

        await EnsureGraphArtifactsExistAsync(request.UploadId, ct).ConfigureAwait(false);

        // Run ingestion in the background so the HTTP request returns 202 immediately.
        // CancellationToken.None is intentional — the work must outlive the HTTP request.
        _ = Task.Run(() => RunGraphIngestionAsync(job, request.UploadId), CancellationToken.None);

        return MapToResponse(job);
    }

    private async Task RunGraphIngestionAsync(IngestionJob job, string uploadId) {
        try {
            await _graphEntityIngestionService.IngestAsync(uploadId, CancellationToken.None).ConfigureAwait(false);
            job.Status = IngestionJobStatus.Completed;
            job.CompletedAtUtc = DateTimeOffset.UtcNow;
            job.FailureReason = null;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Graph import failed for upload {UploadId}.", LogSanitizer.Sanitize(uploadId));
            job.Status = IngestionJobStatus.Failed;
            job.CompletedAtUtc = DateTimeOffset.UtcNow;
            ApplyJobFailure(job, ex.ToString());
        }

        await _repository.UpdateAsync(job, CancellationToken.None).ConfigureAwait(false);
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
            or IngestionJobStatus.PartiallyCompleted
            or IngestionJobStatus.Deleting) {
            _logger.LogWarning(
                "Cannot cancel job {JobId} in terminal status {Status}.",
                jobId,
                job.Status);
            return;
        }

        job.Status = IngestionJobStatus.Cancelled;
        job.CompletedAtUtc = DateTimeOffset.UtcNow;
        job.FailureReason = "Cancelled by user.";
        job.CurrentStage = "cancelled";
        job.StageSetAtUtc = job.CompletedAtUtc;

        await _repository.UpdateAsync(job, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Ingestion job {JobId} cancelled.",
            jobId);
    }

    /// <inheritdoc />
    public async Task FailJobAsync(
        Guid jobId,
        string reason,
        string userId,
        CancellationToken ct = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var job = await _repository.GetByIdAsync(jobId, ct).ConfigureAwait(false);
        if (job is null)
            throw new InvalidOperationException($"Ingestion job '{jobId}' not found.");

        if (job.Status is IngestionJobStatus.Completed
            or IngestionJobStatus.Failed
            or IngestionJobStatus.Cancelled
            or IngestionJobStatus.PartiallyCompleted
            or IngestionJobStatus.Deleting) {
            _logger.LogWarning(
                "Cannot fail job {JobId} in terminal status {Status}.",
                jobId,
                job.Status);
            return;
        }

        job.Status = IngestionJobStatus.Failed;
        job.CompletedAtUtc = DateTimeOffset.UtcNow;
        job.FailureReason = reason;
        job.CurrentStage = "failed";
        job.StageSetAtUtc = job.CompletedAtUtc;

        await _repository.UpdateAsync(job, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Ingestion job {JobId} marked as failed: {Reason}",
            jobId,
            reason);
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

        var failureDetail = job.ErrorsJson;

        return new IngestionJobStatusResponse {
            Id = job.Id,
            JobId = job.IngestionJobId,
            Status = job.Status.ToString(),
            CreatedAtUtc = job.CreatedAtUtc,
            StartedAtUtc = job.StartedAtUtc,
            CompletedAtUtc = job.CompletedAtUtc,
            InputType = job.InputType.ToString(),
            InputRef = job.InputRef,
            ComputeProvider = job.ComputeProvider,
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
            FailureReason = failureDetail ?? job.FailureReason,
            FailureDetail = failureDetail,
            DocIngestionRunId = job.DocIngestionRunId,
            ExpectedChunkCount = job.ExpectedChunkCount,
            IndexedChunkCount = job.IndexedChunkCount,
            CurrentStage = job.CurrentStage,
            StageSetAtUtc = job.StageSetAtUtc
        };
    }

    private async Task EnsureGraphArtifactsExistAsync(string uploadId, CancellationToken ct) {
        var blobPath = $"{GraphEntitiesPrefix}/{uploadId}/entities.json";
        var exists = await _blobStorageService.ExistsAsync(_blobStorageOptions.RawUploadsContainer, blobPath, ct).ConfigureAwait(false);
        if (!exists) {
            throw new InvalidOperationException($"Expected graph entities blob '{blobPath}' was not found.");
        }
    }

    private IngestionJobType MapDocumentType(string documentType) => documentType.Trim().ToLowerInvariant() switch {
        "manual-pdf" => IngestionJobType.PDFManual,
        "spec-dataset" => IngestionJobType.StructuredSpecification,
        "bike-graph" => IngestionJobType.BikeGraph,
        _ => throw new ArgumentException($"Unsupported document type: '{documentType}'.", nameof(documentType))
    };

    /// <inheritdoc />
    public async Task<IngestionJobStatusResponse> TransitionStageAsync(
        Guid jobId,
        IngestionJobStageRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Stage);

        var job = await _repository.GetByIdAsync(jobId, ct).ConfigureAwait(false);
        if (job is null)
        {
            throw new InvalidOperationException($"Ingestion job '{jobId}' not found.");
        }

        if (IsTerminalStatus(job.Status))
        {
            _logger.LogWarning(
                "Rejected stage transition for terminal job {JobId} (status={Status}).",
                jobId, job.Status);
            return MapToResponse(job);
        }

        await _repository.UpdateStageAsync(
            jobId,
            request.Stage,
            request.ChunksProcessed,
            request.TotalChunks,
            request.FailureReason,
            ct).ConfigureAwait(false);

        var stageSetAtUtc = DateTimeOffset.UtcNow;
        if (string.Equals(request.Stage, "cancelled", StringComparison.OrdinalIgnoreCase))
        {
            job.Status = IngestionJobStatus.Cancelled;
            job.CompletedAtUtc = stageSetAtUtc;
            job.CurrentStage = request.Stage;
            job.StageSetAtUtc = stageSetAtUtc;
            job.ExpectedChunkCount ??= request.TotalChunks;
            job.IndexedChunkCount = request.ChunksProcessed;
            job.FailureReason = string.IsNullOrWhiteSpace(request.FailureReason)
                ? "Cancelled by user."
                : request.FailureReason;
            await _repository.UpdateAsync(job, ct).ConfigureAwait(false);
        }
        else if (string.Equals(request.Stage, "completed", StringComparison.OrdinalIgnoreCase))
        {
            // T8: The Python pipeline's fire-and-forget `report_stage("completed")` means
            // "Python done", not "indexing done". The synchronous indexing work runs in
            // `ProcessorArtifactsController.ProcessSearchChunksAsync` after this callback
            // returns, and the controller owns the authoritative terminal transition via
            // `TryTransitionSearchChunkJobToTerminalAsync` (CAS-guarded).
            //
            // Previously this branch asserted `Status = Indexing` for non-terminal jobs,
            // which re-stamped an already-`Indexing` job and left it stuck when the
            // controller's transition did not run (e.g. process recycled mid-index).
            //
            // Now: transition to `Completed` on success, `Failed` on failure, without
            // asserting the prior status. The terminal-status guard above (`IsTerminalStatus`)
            // still prevents flipping a job that already reached `Completed`/`Failed`/
            // `Cancelled`/`PartiallyCompleted`/`Deleting`.
            job.Status = string.IsNullOrWhiteSpace(request.FailureReason)
                ? IngestionJobStatus.Completed
                : IngestionJobStatus.Failed;
            job.CurrentStage = request.Stage;
            job.StageSetAtUtc = stageSetAtUtc;
            if (job.Status == IngestionJobStatus.Completed)
            {
                job.CompletedAtUtc = stageSetAtUtc;
                job.FailureReason = null;
            }
            else
            {
                ApplyJobFailure(job, request.FailureReason!);
            }
            await _repository.UpdateAsync(job, ct).ConfigureAwait(false);
        }
        else
        {
            if (job.Status == IngestionJobStatus.Queued)
            {
                job.Status = IngestionJobStatus.Processing;
                job.StartedAtUtc ??= stageSetAtUtc;
            }

            job.CurrentStage = request.Stage;
            job.StageSetAtUtc = stageSetAtUtc;
            job.ExpectedChunkCount ??= request.TotalChunks;
            job.IndexedChunkCount = request.ChunksProcessed;
            await _repository.UpdateAsync(job, ct).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Ingestion job {JobId} stage transitioned to {Stage} (chunks={ChunksProcessed}/{TotalChunks}).",
            jobId, request.Stage, request.ChunksProcessed, request.TotalChunks);

        return MapToResponse(job);
    }

    private static bool IsFailedStatus(IngestionJobStatus status) =>
        status is IngestionJobStatus.Failed or IngestionJobStatus.Cancelled;

    private async Task DeleteAssociatedAssetsAsync(IngestionJob job, CancellationToken ct) {
        var uploadId = job.InputRef;
        if (string.IsNullOrWhiteSpace(uploadId)) {
            return;
        }

        var artifacts = await GetDeleteArtifactsAsync(job, uploadId, ct).ConfigureAwait(false);
        var chunks = await GetDeleteChunksAsync(job, uploadId, artifacts, ct).ConfigureAwait(false);
        var chunkIds = chunks
            .Select(static chunk => chunk.ChunkId)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var fallbackId in BuildFallbackChunkIds(job, uploadId)) {
            chunkIds.Add(fallbackId);
        }

        if (chunkIds.Count > 0) {
            await RunBestEffortAsync(
                () => _searchDocumentService.DeleteDocumentsAsync(chunkIds),
                "delete Azure Search documents for ingestion job",
                job.IngestionJobId).ConfigureAwait(false);
        }

        foreach (var artifact in artifacts) {
            await RunBestEffortAsync(
                () => _chunkRepository.DeleteByArtifactIdAsync(artifact.IndexedArtifactId, ct),
                "delete indexed chunks for artifact",
                artifact.IndexedArtifactId).ConfigureAwait(false);
        }

        await RunBestEffortAsync(
            () => _chunkRepository.DeleteByIngestionJobIdAsync(job.IngestionJobId, ct),
            "delete indexed chunks for ingestion job",
            job.IngestionJobId).ConfigureAwait(false);
        await RunBestEffortAsync(
            () => _chunkRepository.DeleteByUploadIdAsync(uploadId, ct),
            "delete indexed chunks for upload",
            uploadId).ConfigureAwait(false);

        foreach (var artifact in artifacts) {
            await RunBestEffortAsync(
                () => _artifactRepository.DeleteByIdAsync(artifact.IndexedArtifactId, ct),
                "delete indexed artifact",
                artifact.IndexedArtifactId).ConfigureAwait(false);
        }

        await DeleteBlobIfExistsAsync(IngestionBlobPaths.BuildRawUploadBlobName(uploadId, ToDocumentType(job.InputType)), ct).ConfigureAwait(false);
        await DeleteBlobIfExistsAsync(BuildSearchChunksBlobPath(uploadId), ct).ConfigureAwait(false);
        await DeleteBlobIfExistsAsync(BuildGraphEntitiesBlobPath(uploadId), ct).ConfigureAwait(false);

        if (Guid.TryParse(uploadId, out var sourceDocumentId)) {
            await RunBestEffortAsync(
                () => _graphRepository.DeleteByDocumentAsync(sourceDocumentId, ct),
                "delete graph rows for source document",
                sourceDocumentId).ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<IndexedArtifact>> GetDeleteArtifactsAsync(
        IngestionJob job,
        string uploadId,
        CancellationToken ct) {
        var byJob = await _artifactRepository.GetByIngestionJobIdAsync(job.IngestionJobId, ct).ConfigureAwait(false);
        var byUpload = await _artifactRepository.GetByUploadIdAsync(uploadId, ct).ConfigureAwait(false);

        return byJob.Concat(byUpload)
            .GroupBy(static artifact => artifact.IndexedArtifactId)
            .Select(static group => group.First())
            .ToArray();
    }

    private async Task<IReadOnlyList<IndexedChunk>> GetDeleteChunksAsync(
        IngestionJob job,
        string uploadId,
        IReadOnlyList<IndexedArtifact> artifacts,
        CancellationToken ct) {
        var chunks = new List<IndexedChunk>();
        foreach (var artifact in artifacts) {
            chunks.AddRange(await _chunkRepository.GetByArtifactIdAsync(artifact.IndexedArtifactId, ct).ConfigureAwait(false));
        }

        chunks.AddRange(await _chunkRepository.GetByIngestionJobIdAsync(job.IngestionJobId, ct).ConfigureAwait(false));
        chunks.AddRange(await _chunkRepository.GetByUploadIdAsync(uploadId, ct).ConfigureAwait(false));

        return chunks
            .GroupBy(static chunk => chunk.ChunkId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
    }

    private static IEnumerable<string> BuildFallbackChunkIds(IngestionJob job, string uploadId) {
        var expectedChunkCount = job.ExpectedChunkCount ?? 0;
        if (expectedChunkCount <= 0) {
            yield break;
        }

        var suffix = job.InputType switch {
            IngestionJobType.PDFManual => "pdf",
            IngestionJobType.StructuredSpecification => "csv",
            _ => null
        };

        if (suffix is null) {
            yield break;
        }

        for (var i = 0; i < expectedChunkCount; i++) {
            yield return $"{uploadId}-{suffix}-{i}";
        }
    }

    private async Task DeleteBlobIfExistsAsync(string blobPath, CancellationToken ct) {
        await RunBestEffortAsync(
            async () => await _blobStorageService.DeleteIfExistsAsync(_blobStorageOptions.RawUploadsContainer, blobPath, ct).ConfigureAwait(false),
            "delete ingestion blob",
            blobPath).ConfigureAwait(false);
    }

    private async Task RunBestEffortAsync(
        Func<Task> action,
        string operation,
        object identifier) {
        try {
            await action().ConfigureAwait(false);
        }
        catch (Exception ex) {
            _logger.LogWarning(
                ex,
                "Best-effort cleanup failed while attempting to {Operation} ({Identifier}).",
                operation,
                LogSanitizer.Sanitize(Convert.ToString(identifier) ?? string.Empty));
        }
    }

    private static string ToDocumentType(IngestionJobType inputType) => inputType switch {
        IngestionJobType.PDFManual => "manual-pdf",
        _ => "spec-dataset"
    };

    private static string BuildSearchChunksBlobPath(string uploadId) => $"{uploadId}/chunks.jsonl";

    private static string BuildGraphEntitiesBlobPath(string uploadId) => $"graph-entities/{uploadId}/entities.json";

    private static IngestionJobType GetPrimaryInputType(string documentType) => documentType switch {
        "manual-pdf" => IngestionJobType.PDFManual,
        "spec-dataset" => IngestionJobType.StructuredSpecification,
        _ => throw new ArgumentException($"Unsupported pending document type '{documentType}'.", nameof(documentType))
    };

    private static bool IsTerminalStatus(IngestionJobStatus status) =>
        status is IngestionJobStatus.Completed
            or IngestionJobStatus.Failed
            or IngestionJobStatus.Cancelled
            or IngestionJobStatus.PartiallyCompleted
            or IngestionJobStatus.Deleting;

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
        // A job whose deletion is in-flight is not a pending workflow and must
        // never surface in the pending storage files list.
        if (job is not null && job.Status == IngestionJobStatus.Deleting) {
            return false;
        }

        return job is null
               || job.Status is IngestionJobStatus.Failed
               or IngestionJobStatus.Cancelled;
    }

    private static void ApplyJobFailure(IngestionJob job, string detail) {
        if (string.IsNullOrWhiteSpace(detail)) {
            return;
        }

        job.ErrorsJson = detail.Trim();
        job.ErrorMessage = FirstFailureLine(detail);
        job.FailureReason = detail.Length <= 2000 ? detail : detail[..2000];
    }

    private static string FirstFailureLine(string detail) {
        var line = detail.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        return string.IsNullOrWhiteSpace(line) ? detail.Trim() : line;
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
