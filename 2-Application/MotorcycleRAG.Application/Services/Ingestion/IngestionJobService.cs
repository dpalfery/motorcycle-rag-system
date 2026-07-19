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
using MotorcycleRAG.Contracts.Models.DTOs.Ingestion;

namespace MotorcycleRAG.Application.Services.Ingestion;

/// <summary>
/// Orchestrates the ingestion job lifecycle: creation, status retrieval, and cancellation.
/// Delegates persistence to <see cref="IIngestionJobRepository"/>; local processor execution
/// is initiated by Admin Desktop and reported through processor callback endpoints.
/// </summary>
public sealed class IngestionJobService : IIngestionJobService {
    private const string PdfSourceFileName = "source.pdf";
    private const string GraphEntitiesPrefix = "graph-entities";
    private const string NeedsManualMetadataStage = "needs-manual-metadata";
    private const string MetadataResumingStage = "resuming";
    private const int MetadataRequiredFieldCount = 4;
    private const int MetadataMaxJsonLength = 10_000;
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
    private readonly GraphIngestionChannel _graphIngestionChannel;
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
        GraphIngestionChannel graphIngestionChannel,
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
        _graphIngestionChannel = graphIngestionChannel ?? throw new ArgumentNullException(nameof(graphIngestionChannel));
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
                _blobStorageOptions.RawUploadsContainer);
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

        // Build all (InputRef, InputType) pairs for batch query
        var pairs = new List<(string InputRef, IngestionJobType InputType)>(candidates.Count * 2);
        foreach (var candidate in candidates.Values) {
            pairs.Add((candidate.UploadId, GetPrimaryInputType(candidate.DocumentType)));
            if (SupportsGraphImport(candidate.DocumentType)) {
                pairs.Add((candidate.UploadId, IngestionJobType.BikeGraph));
            }
        }

        var latestJobs = await _repository.GetLatestByInputRefsAsync(pairs, ct).ConfigureAwait(false);
        var jobsByPair = latestJobs.ToDictionary(
            j => (j.InputRef, j.InputType),
            j => j);

        var pendingFiles = new List<PendingStorageFileDto>(candidates.Count);
        foreach (var candidate in candidates.Values) {
            var primaryType = GetPrimaryInputType(candidate.DocumentType);
            jobsByPair.TryGetValue((candidate.UploadId, primaryType), out var latestJob);

            IngestionJob? latestGraphJob = null;
            if (SupportsGraphImport(candidate.DocumentType)) {
                jobsByPair.TryGetValue((candidate.UploadId, IngestionJobType.BikeGraph), out latestGraphJob);
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
            LogSanitizer.Sanitize(uploadId),  // codeql[cs/log-forging]
            LogSanitizer.Sanitize(documentType));  // codeql[cs/log-forging]
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
            job.RollbackDeletion($"Background cleanup failed: {ex.Message}");
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

        if (!IsRetryableStatus(job.Status)) {
            throw new InvalidOperationException(
                "Only failed, cancelled, or awaiting-metadata ingestion jobs can be retried.");
        }

        // PDFManual jobs have their source file stored as a blob in cloud storage
        // ({uploadId}/source.pdf). Reset the job to Queued so Admin Desktop's poller
        // detects it and re-processes from the existing blob — no re-upload needed.
        if (job.InputType == IngestionJobType.PDFManual) {
            var documentType = ToDocumentType(job.InputType);
            var blobPath = IngestionBlobPaths.BuildRawUploadBlobName(job.InputRef, documentType);

            // Guard: the source blob must still exist in storage. If it was cleaned up
            // (e.g. by a prior delete cycle or retention policy), the job cannot be retried
            // without a fresh upload.
            var blobExists = await _blobStorageService
                .ExistsAsync(_blobStorageOptions.RawUploadsContainer, blobPath, ct)
                .ConfigureAwait(false);
            if (!blobExists) {
                throw new InvalidOperationException(
                    $"The source file for this ingestion job ('{blobPath}') " +
                    "was not found in blob storage. Re-upload the PDF and start a new ingestion job.");
            }

            // Reset the job to Queued and clear all error/stage information so the
            // processor starts fresh from the existing blob. QueueForRetry already clears
            // ErrorMessage/ErrorsJson/failure metadata internally.
            job.QueueForRetry();

            await _repository.UpdateAsync(job, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Ingestion job {JobId} reset to Queued for retry by user {UserId}. " +
                "Source blob: {BlobPath}.",
                jobId,
                userId,
                blobPath);

            return MapToResponse(job);
        }

        // BikeGraph and StructuredSpecification jobs are local-first: their original source
        // files are managed on the Admin Desktop machine. The C# API cannot re-queue from
        // the local watch folder — Admin Desktop must re-queue the file so the processor
        // picks it up and creates a fresh job.
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

        var job = IngestionJob.Create(
            MapDocumentType(request.DocumentType),
            request.UploadId,
            userId,
            request.SourceFileName,
            "AdminLocalProcessor",
            request.ProcessorRunId);

        job = await _repository.CreateAsync(job, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Ingestion job {JobId} created for input type {InputType}.",
            job.IngestionJobId,
            job.InputType);

        _logger.LogInformation(
            "Queued ingestion job {JobId} for processor run {ProcessorRunId}.",
            LogSanitizer.Sanitize(job.IngestionJobId),
            LogSanitizer.Sanitize(request.ProcessorRunId));  // codeql[cs/log-forging]

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

        var job = IngestionJob.Create(
            IngestionJobType.BikeGraph,
            request.UploadId,
            userId,
            sourceFileName: null,
            computeProvider: "AdminLocalProcessor",
            docIngestionRunId: null,
            initialStatus: IngestionJobStatus.Processing,
            startedAtUtc: DateTimeOffset.UtcNow);

        job = await _repository.CreateAsync(job, ct).ConfigureAwait(false);

        await EnsureGraphArtifactsExistAsync(request.UploadId, ct).ConfigureAwait(false);

        // Enqueue the job for background graph ingestion with bounded backpressure.
        // GraphIngestionBackgroundService (singleton) drains the channel and runs the work
        // outside the HTTP request lifecycle. WriteAsync awaits if the bounded channel is
        // full, replacing the previous unbounded fire-and-forget Task.Run. The job's
        // InputRef already holds request.UploadId, so the consumer can rehydrate everything
        // it needs from the IngestionJob entity alone.
        await _graphIngestionChannel.Writer.WriteAsync(job, ct).ConfigureAwait(false);

        return MapToResponse(job);
    }

    /// <summary>
    /// Executes graph ingestion for a single job dequeued from <see cref="GraphIngestionChannel"/>.
    /// Called by <see cref="GraphIngestionBackgroundService"/> from a per-item DI scope so that
    /// scoped repository/storage dependencies are resolved correctly. Errors are captured and
    /// persisted as a Failed status inside <see cref="RunGraphIngestionAsync"/>; this method
    /// does not throw for ordinary ingestion failures.
    /// </summary>
    /// <param name="job">The job to process (its <see cref="IngestionJob.InputRef"/> carries the upload id).</param>
    public Task ProcessGraphIngestionJobAsync(IngestionJob job) {
        ArgumentNullException.ThrowIfNull(job);
        return RunGraphIngestionAsync(job, job.InputRef ?? string.Empty);
    }

    private async Task RunGraphIngestionAsync(IngestionJob job, string uploadId) {
        try {
            await _graphEntityIngestionService.IngestAsync(uploadId, CancellationToken.None).ConfigureAwait(false);
            job.Complete();
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Graph import failed for upload {UploadId}.", uploadId);
            job.Fail(ex.ToString());
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

        job.Cancel();

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

        job.UpdateStage("failed", null, null);
        job.Fail(reason);

        await _repository.UpdateAsync(job, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Ingestion job {JobId} marked as failed: {Reason}",
            LogSanitizer.Sanitize(jobId),
            LogSanitizer.Sanitize(reason));  // codeql[cs/log-forging]
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
            SourceFileName = job.SourceFileName,
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
            StageSetAtUtc = job.StageSetAtUtc,
            RequiresManualMetadata = job.Status == IngestionJobStatus.AwaitingMetadata
                || string.Equals(job.CurrentStage, NeedsManualMetadataStage, StringComparison.OrdinalIgnoreCase)
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
            // Record chunk progress through the domain method (UpdateStage applies the
            // same ExpectedChunkCount ??= / IndexedChunkCount = semantics the service used
            // to set directly), then perform the terminal Cancel transition. Reordering is
            // required because UpdateStage rejects terminal statuses; Cancel overwrites
            // CurrentStage to "cancelled", matching prior behavior.
            job.UpdateStage(request.Stage, request.ChunksProcessed, request.TotalChunks, stageSetAtUtc);
            job.Cancel(string.IsNullOrWhiteSpace(request.FailureReason) ? "Cancelled by user." : request.FailureReason!, stageSetAtUtc);
            await _repository.UpdateAsync(job, ct).ConfigureAwait(false);
        }
        else if (string.Equals(request.Stage, "failed", StringComparison.OrdinalIgnoreCase)
                 || ShouldTreatLocalProcessorFailureAsTerminal(job, request))
        {
            var failureDetail = string.IsNullOrWhiteSpace(request.FailureReason)
                ? $"Processor reported failure during stage '{request.Stage}'."
                : request.FailureReason!;
            // Compat path: local-processor mid-stage failures may report the active stage
            // (e.g. "chunking") with a FailureReason. Normalize CurrentStage to "failed"
            // before the terminal transition so status readers see a consistent failed stage.
            var failedStage = string.Equals(request.Stage, "failed", StringComparison.OrdinalIgnoreCase)
                ? request.Stage
                : "failed";
            job.UpdateStage(failedStage, request.ChunksProcessed, request.TotalChunks, stageSetAtUtc);
            job.Fail(failureDetail, stageSetAtUtc);

            ApplyJobFailure(job, failureDetail);
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
            //
            // Update stage fields while the job is still non-terminal; Complete/Fail make
            // the job terminal and UpdateStage would then reject the write.
            job.UpdateStage(request.Stage, request.ChunksProcessed, request.TotalChunks, stageSetAtUtc);
            if (string.IsNullOrWhiteSpace(request.FailureReason))
            {
                job.Complete(stageSetAtUtc);
            }
            else
            {
                job.Fail(request.FailureReason!, stageSetAtUtc);
                ApplyJobFailure(job, request.FailureReason!);
            }
            await _repository.UpdateAsync(job, ct).ConfigureAwait(false);
        }
        else if (string.Equals(request.Stage, NeedsManualMetadataStage, StringComparison.OrdinalIgnoreCase))
        {
            // The processor's automated metadata extraction could not determine all required
            // fields after sampling the maximum number of pages. Transition the job to the
            // paused AwaitingMetadata state so the admin UI can surface a manual-entry modal.
            // This is NOT a terminal state — the job resumes once an admin submits metadata.
            job.PauseForMetadata(request.FailureReason, request.Stage, stageSetAtUtc);
            await _repository.UpdateAsync(job, ct).ConfigureAwait(false);
        }
        else
        {
            job.UpdateStage(request.Stage, request.ChunksProcessed, request.TotalChunks, stageSetAtUtc);
            await _repository.UpdateAsync(job, ct).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Ingestion job {JobId} stage transitioned to {Stage} (chunks={ChunksProcessed}/{TotalChunks}, failureReason={FailureReason}).",
            jobId,
            LogSanitizer.Sanitize(request.Stage),  // codeql[cs/log-forging]
            request.ChunksProcessed,
            request.TotalChunks,
            LogSanitizer.Sanitize(request.FailureReason));  // codeql[cs/log-forging]

        return MapToResponse(job);
    }

    /// <inheritdoc />
    public async Task<IngestionJobStatusResponse> SubmitManualMetadataAsync(
        Guid jobId,
        string metadataJson,
        string userId,
        CancellationToken ct = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(metadataJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (metadataJson.Length > MetadataMaxJsonLength) {
            throw new ArgumentException(
                $"metadataJson must not exceed {MetadataMaxJsonLength} characters.",
                nameof(metadataJson));
        }

        // Validate that the input is well-formed JSON, is a top-level object, AND has all four
        // required fields populated. Throws ArgumentException with a descriptive message listing
        // the missing fields on validation failure — the admin must not be able to resume with
        // incomplete metadata.
        var parsed = ParseMetadataOrThrow(metadataJson);

        var job = await _repository.GetByIdAsync(jobId, ct).ConfigureAwait(false);
        if (job is null) {
            // KeyNotFoundException (not InvalidOperationException) so the controller can
            // distinguish "job not found" (404) from DB failures (500).
            throw new KeyNotFoundException($"Ingestion job '{jobId}' not found.");
        }

        // Persist the metadata blob. This is idempotent — a duplicate submission with the
        // same value overwrites harmlessly. UpdateMetadataAsync touches only the
        // MetadataJson column so it cannot clobber concurrent stage/status writes.
        job.SetMetadata(metadataJson);
        await _repository.UpdateMetadataAsync(jobId, metadataJson, ct).ConfigureAwait(false);

        // CAS-guarded transition: only flip to Processing if the row is still in
        // AwaitingMetadata. This prevents lost updates / TOCTOU races where a concurrent
        // request or the processor self-recovered between our read and this write. If the
        // CAS does not match (returns false), the metadata was still persisted — this is the
        // idempotent duplicate path: the resume is not re-triggered.
        var resumed = await _repository.TryTransitionFromAwaitingMetadataAsync(
            jobId, MetadataResumingStage, ct).ConfigureAwait(false);

        if (resumed) {
            // Reflect the transition in the in-memory entity for the response mapping.
            job.ResumeFromMetadata(MetadataResumingStage);

            // Processor resume: In this architecture Admin Desktop orchestrates the Python
            // local-processing-service (it starts the processor and the processor reports
            // back via stage callbacks). The resume follows the same pattern: Admin Desktop
            // polls /api/ingestion/jobs every 15 s, detects status=Processing +
            // currentStage=resuming, and calls the Python POST /process/pdf endpoint with
            // the job_id and metadata override. The C# API does NOT call the Python service
            // directly — doing so would violate the existing orchestration boundary.
            _logger.LogInformation(
                "Manual metadata submitted for job {JobId} by user {UserId}. Transitioned AwaitingMetadata -> Processing. " +
                "FillRate={FillRate:F2}, IsComplete={IsComplete}. Admin Desktop will detect the resume stage and trigger the processor.",
                jobId, userId, parsed.FillRate, parsed.IsComplete);
        }
        else {
            // The CAS did not match — the job was no longer in AwaitingMetadata. The metadata
            // blob was still persisted above. Update the in-memory status from the stored value
            // so the response is accurate.
            _logger.LogInformation(
                "Metadata updated for job {JobId} by user {UserId} but status is {Status} (not AwaitingMetadata). Resume not triggered.",
                jobId, userId, job.Status);
        }

        return MapToResponse(job);
    }

    /// <inheritdoc />
    public async Task<IngestionJobMetadataResponse?> GetJobMetadataAsync(
        Guid jobId,
        string userId,
        CancellationToken ct = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var job = await _repository.GetByIdAsync(jobId, ct).ConfigureAwait(false);
        if (job is null) {
            return null;
        }

        // No metadata recorded yet — return an empty response with IsComplete = false so
        // the admin UI can render an empty form.
        if (string.IsNullOrWhiteSpace(job.MetadataJson)) {
            _logger.LogInformation(
                "Metadata view requested for job {JobId} by user {UserId}: no metadata recorded yet.",
                jobId, userId);
            return new IngestionJobMetadataResponse {
                JobId = jobId,
                IsComplete = false,
                FillRate = 0.0,
                RawJson = null
            };
        }

        // Parse the stored JSON into structured fields. If the stored blob is corrupt
        // (should not happen under normal operation), return the raw JSON with
        // IsComplete = false rather than throwing — the admin can still see and fix the data.
        var parsed = TryParseMetadata(job.MetadataJson);
        if (parsed is null) {
            _logger.LogWarning(
                "Stored metadata JSON for job {JobId} could not be parsed (requested by user {UserId}). Returning raw blob.",
                jobId, userId);
            return new IngestionJobMetadataResponse {
                JobId = jobId,
                IsComplete = false,
                FillRate = 0.0,
                RawJson = job.MetadataJson
            };
        }

        _logger.LogInformation(
            "Metadata view for job {JobId} requested by user {UserId}: FillRate={FillRate:F2}, IsComplete={IsComplete}.",
            jobId, userId, parsed.FillRate, parsed.IsComplete);

        return new IngestionJobMetadataResponse {
            JobId = jobId,
            Make = parsed.Make,
            Model = parsed.Model,
            Year = parsed.Year,
            Category = parsed.Category,
            Tags = parsed.Tags,
            FillRate = parsed.FillRate,
            IsComplete = parsed.IsComplete,
            RawJson = job.MetadataJson
        };
    }

    private static bool IsFailedStatus(IngestionJobStatus status) =>
        status is IngestionJobStatus.Failed or IngestionJobStatus.Cancelled;

    /// <summary>
    /// Determines whether a job is in a state from which retry is permitted:
    /// Failed, Cancelled, or AwaitingMetadata (the paused "needs manual metadata" state).
    /// </summary>
    private static bool IsRetryableStatus(IngestionJobStatus status) =>
        status is IngestionJobStatus.Failed
            or IngestionJobStatus.Cancelled
            or IngestionJobStatus.AwaitingMetadata;

    private static bool ShouldTreatLocalProcessorFailureAsTerminal(
        IngestionJob job,
        IngestionJobStageRequest request) {
        if (string.IsNullOrWhiteSpace(request.FailureReason)) {
            return false;
        }

        if (string.Equals(request.Stage, "completed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(request.Stage, "cancelled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(request.Stage, "failed", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        var provider = job.ComputeProvider?.Trim();
        return !string.IsNullOrWhiteSpace(provider)
               && provider.Contains("local", StringComparison.OrdinalIgnoreCase);
    }

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

        if (artifacts.Count > 0) {
            var deleteArtifactIds = artifacts.Select(static a => a.IndexedArtifactId).ToArray();
            await RunBestEffortAsync(
                () => _chunkRepository.DeleteByArtifactIdsAsync(deleteArtifactIds, ct),
                "delete indexed chunks for artifacts",
                job.IngestionJobId).ConfigureAwait(false);
        }

        await RunBestEffortAsync(
            () => _chunkRepository.DeleteByIngestionJobIdAsync(job.IngestionJobId, ct),
            "delete indexed chunks for ingestion job",
            job.IngestionJobId).ConfigureAwait(false);
        await RunBestEffortAsync(
            () => _chunkRepository.DeleteByUploadIdAsync(uploadId, ct),
            "delete indexed chunks for upload",
            uploadId).ConfigureAwait(false);

        if (artifacts.Count > 0) {
            var deleteArtifactIds = artifacts.Select(static a => a.IndexedArtifactId).ToArray();
            await RunBestEffortAsync(
                () => _artifactRepository.DeleteByIdsAsync(deleteArtifactIds, ct),
                "delete indexed artifacts",
                job.IngestionJobId).ConfigureAwait(false);
        }

        // The three blob deletions are independent (distinct paths) and each is individually
        // wrapped in RunBestEffortAsync, so they can run concurrently. This shaves up to two
        // round-trip latencies off job teardown compared to awaiting them sequentially.
        await Task.WhenAll(
            DeleteBlobIfExistsAsync(IngestionBlobPaths.BuildRawUploadBlobName(uploadId, ToDocumentType(job.InputType)), ct),
            DeleteBlobIfExistsAsync(BuildSearchChunksBlobPath(uploadId), ct),
            DeleteBlobIfExistsAsync(BuildGraphEntitiesBlobPath(uploadId), ct)).ConfigureAwait(false);

        if (Guid.TryParse(uploadId, out var sourceDocumentId)) {
            await RunBestEffortAsync(
                () => _graphRepository.DeleteByDocumentAsync(sourceDocumentId, ct),
                "delete graph rows for source document",
                sourceDocumentId).ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<IndexedArtifactDto>> GetDeleteArtifactsAsync(
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

    private async Task<IReadOnlyList<IndexedChunkDto>> GetDeleteChunksAsync(
        IngestionJob job,
        string uploadId,
        IReadOnlyList<IndexedArtifactDto> artifacts,
        CancellationToken ct) {
        var artifactIds = artifacts.Select(static a => a.IndexedArtifactId).ToArray();
        var chunks = new List<IndexedChunkDto>();
        if (artifactIds.Length > 0) {
            chunks.AddRange(await _chunkRepository.GetByArtifactIdsAsync(artifactIds, ct).ConfigureAwait(false));
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
                Convert.ToString(identifier) ?? string.Empty);
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

        job.RecordFailure(detail);
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

    // --- Metadata JSON parsing helpers ---

    /// <summary>
    /// Lightweight container for parsed motorcycle metadata fields and computed fill rate.
    /// </summary>
    private sealed record ParsedMetadata(
        string? Make,
        string? Model,
        int? Year,
        string? Category,
        IReadOnlyList<string> Tags,
        double FillRate,
        bool IsComplete);

    /// <summary>
    /// Parses a metadata JSON string and throws <see cref="ArgumentException"/> when the
    /// input is not valid JSON, is not a JSON object, or is missing one or more required
    /// fields (make, model, year, category). Used by the submit path where invalid input
    /// must be rejected with a clear, actionable error message.
    /// </summary>
    private static ParsedMetadata ParseMetadataOrThrow(string metadataJson) {
        JsonDocument doc;
        try {
            doc = JsonDocument.Parse(metadataJson);
        }
        catch (JsonException ex) {
            throw new ArgumentException("metadataJson is not valid JSON.", ex);
        }

        using (doc) {
            if (doc.RootElement.ValueKind != JsonValueKind.Object) {
                throw new ArgumentException("metadataJson must be a JSON object.");
            }

            var parsed = ExtractMetadata(doc.RootElement);

            // Enforce required-field completeness: an admin must not be able to resume the
            // pipeline with empty or partial metadata. The automated extraction path already
            // guarantees completeness before transitioning to AwaitingMetadata — but a manual
            // submission is admin-controlled, so we validate here as a defence-in-depth guard.
            if (!parsed.IsComplete) {
                var missing = GetMissingMetadataFields(parsed);
                throw new ArgumentException(
                    $"The submitted metadata is incomplete. All four required fields (make, model, year, category) " +
                    $"must be populated. Missing: {string.Join(", ", missing)}.");
            }

            return parsed;
        }
    }

    /// <summary>
    /// Returns the names of the required metadata fields that are missing or empty.
    /// </summary>
    private static List<string> GetMissingMetadataFields(ParsedMetadata parsed) {
        var missing = new List<string>(MetadataRequiredFieldCount);
        if (string.IsNullOrWhiteSpace(parsed.Make)) {
            missing.Add("make");
        }

        if (string.IsNullOrWhiteSpace(parsed.Model)) {
            missing.Add("model");
        }

        if (!parsed.Year.HasValue || parsed.Year.Value <= 0) {
            missing.Add("year");
        }

        if (string.IsNullOrWhiteSpace(parsed.Category)) {
            missing.Add("category");
        }

        return missing;
    }

    /// <summary>
    /// Attempts to parse a metadata JSON string. Returns null when the input is not valid
    /// JSON or is not a JSON object. Used by the GET path where corrupt stored data must
    /// not cause a 500 error.
    /// </summary>
    private static ParsedMetadata? TryParseMetadata(string metadataJson) {
        try {
            using var doc = JsonDocument.Parse(metadataJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) {
                return null;
            }

            return ExtractMetadata(doc.RootElement);
        }
        catch (JsonException) {
            return null;
        }
    }

    /// <summary>
    /// Extracts make, model, year, category, and tags from a JSON object element and
    /// computes the fill rate (fraction of the four required fields that are populated).
    /// </summary>
    private static ParsedMetadata ExtractMetadata(JsonElement root) {
        var make = TryGetMetadataString(root, "make");
        var model = TryGetMetadataString(root, "model");
        var year = TryGetMetadataInt(root, "year");
        var category = TryGetMetadataString(root, "category");
        var tags = TryGetMetadataStringList(root, "tags");

        var filled = 0;
        if (!string.IsNullOrWhiteSpace(make)) filled++;
        if (!string.IsNullOrWhiteSpace(model)) filled++;
        if (year.HasValue && year.Value > 0) filled++;
        if (!string.IsNullOrWhiteSpace(category)) filled++;

        var fillRate = (double)filled / MetadataRequiredFieldCount;

        return new ParsedMetadata(
            NormalizeMetadataString(make),
            NormalizeMetadataString(model),
            year,
            NormalizeMetadataString(category),
            tags,
            fillRate,
            filled == MetadataRequiredFieldCount);
    }

    private static string? TryGetMetadataString(JsonElement root, string propertyName) {
        if (root.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String) {
            return prop.GetString();
        }

        return null;
    }

    private static int? TryGetMetadataInt(JsonElement root, string propertyName) {
        if (!root.TryGetProperty(propertyName, out var prop)) {
            return null;
        }

        // Accept year as a JSON number first, then fall back to a numeric string (some
        // LLMs emit "2023" instead of 2023).
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var numValue)) {
            return numValue;
        }

        if (prop.ValueKind == JsonValueKind.String) {
            var str = prop.GetString();
            if (int.TryParse(str, out var parsed)) {
                return parsed;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> TryGetMetadataStringList(JsonElement root, string propertyName) {
        if (!root.TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.Array) {
            return [];
        }

        var list = new List<string>();
        foreach (var item in prop.EnumerateArray()) {
            if (item.ValueKind == JsonValueKind.String) {
                var value = item.GetString();
                if (!string.IsNullOrWhiteSpace(value)) {
                    list.Add(value.Trim());
                }
            }
        }

        return list;
    }

    private static string? NormalizeMetadataString(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
