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
/// Delegates persistence to <see cref="IIngestionJobRepository"/> and pipeline triggering
/// to <see cref="IFabricPipelineService"/> or <see cref="ILocalPipelineService"/> based on
/// the configured <see cref="ProcessingMode"/>.
/// </summary>
public sealed class IngestionJobService : IIngestionJobService {
    private const string PdfSourceFileName = "source.pdf";

    private readonly IIngestionJobRepository _repository;
    private readonly IBlobStorageService _blobStorageService;
    private readonly BlobStorageOptions _blobStorageOptions;
    private readonly ILocalPipelineService _pipelineService;
    private readonly IngestionOptions _options;
    private readonly ILogger<IngestionJobService> _logger;

    public IngestionJobService(
        IIngestionJobRepository repository,
        IBlobStorageService blobStorageService,
        ILocalPipelineService pipelineService,
        IOptions<BlobStorageOptions> blobStorageOptions,
        IOptions<IngestionOptions> options,
        ILogger<IngestionJobService> logger) {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _blobStorageService = blobStorageService ?? throw new ArgumentNullException(nameof(blobStorageService));
        _pipelineService = pipelineService ?? throw new ArgumentNullException(nameof(pipelineService));
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
            var latestJob = await _repository.GetLatestByInputRefAsync(candidate.UploadId, ct).ConfigureAwait(false);
            if (!ShouldIncludeAsPending(latestJob)) {
                continue;
            }

            pendingFiles.Add(candidate with {
                LastKnownJobStatus = latestJob?.Status.ToString(),
                FailureReason = latestJob?.FailureReason
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
        return jobs.Select(MapToResponse).ToArray();
    }

    /// <inheritdoc />
    public async Task<IngestionJobStatusResponse> StartJobAsync(
        IngestionJobStartRequest request,
        string userId,
        CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var inputType = request.DocumentType switch {
            "manual-pdf" => IngestionJobType.PDFManual,
            "spec-dataset" => IngestionJobType.StructuredSpecification,
            _ => throw new ArgumentException($"Unsupported document type: '{request.DocumentType}'.", nameof(request))
        };

        var pipelineId = inputType switch {
            IngestionJobType.PDFManual => _options.PdfPipelineId,
            IngestionJobType.StructuredSpecification => _options.CsvPipelineId,
            _ => throw new InvalidOperationException($"No pipeline configured for input type '{inputType}'.")
        };

        var job = new IngestionJob {
            InputType = inputType,
            InputRef = request.UploadId,
            CreatedBySubject = userId,
            Status = IngestionJobStatus.Queued
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

    private static bool ShouldIncludeAsPending(IngestionJob? job) {
        return job is null
               || job.Status is IngestionJobStatus.Failed
               or IngestionJobStatus.Cancelled;
    }

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
