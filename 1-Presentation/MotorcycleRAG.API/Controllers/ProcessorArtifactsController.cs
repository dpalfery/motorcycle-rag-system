using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Application.Services.Ingestion;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Core.Utilities;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.API.Controllers;

/// <summary>
/// Accepts processed artifacts (chunks.jsonl, entities.json) from the Python local processing service via M2M auth.
/// </summary>
/// <remarks>
/// All endpoints require the <c>mcr-api-local-processor</c> policy (M2M client credentials + File.Upload.All role).
/// File names and user-provided paths are never logged — only opaque identifiers (uploadId, artifactType).
/// </remarks>
[ApiController]
[Route("api/ingestion")]
[Produces("application/json")]
[Authorize(Policy = "mcr-api-local-processor")]
[SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "Controllers must be public for discovery")]
[EnableRateLimiting("ingestion-jobs")]
public sealed class ProcessorArtifactsController : ControllerBase
{
    private readonly IBlobStorageService _blobStorageService;
    private readonly BlobStorageOptions _blobStorageOptions;
    private readonly IChunkIndexingService _chunkIndexingService;
    private readonly IIngestionJobRepository _jobRepository;
    private readonly IIndexedArtifactRepository _artifactRepository;
    private readonly IIndexedChunkRepository _chunkRepository;
    private readonly IIngestionSourceAccessTokenService _sourceAccessTokenService;
    private readonly IIngestionJobService _ingestionJobService;
    private readonly ILogger<ProcessorArtifactsController> _logger;

    /// <summary>Maximum upload size in bytes (500 MB).</summary>
    private const long MaxFileSizeBytes = 500L * 1024 * 1024;

    public ProcessorArtifactsController(
        IBlobStorageService blobStorageService,
        IOptions<BlobStorageOptions> blobStorageOptions,
        IChunkIndexingService chunkIndexingService,
        IIngestionJobRepository jobRepository,
        IIndexedArtifactRepository artifactRepository,
        IIndexedChunkRepository chunkRepository,
        IIngestionSourceAccessTokenService sourceAccessTokenService,
        IIngestionJobService ingestionJobService,
        ILogger<ProcessorArtifactsController> logger)
    {
        _blobStorageService = blobStorageService ?? throw new ArgumentNullException(nameof(blobStorageService));
        _blobStorageOptions = blobStorageOptions?.Value ?? throw new ArgumentNullException(nameof(blobStorageOptions));
        _chunkIndexingService = chunkIndexingService ?? throw new ArgumentNullException(nameof(chunkIndexingService));
        _jobRepository = jobRepository ?? throw new ArgumentNullException(nameof(jobRepository));
        _artifactRepository = artifactRepository ?? throw new ArgumentNullException(nameof(artifactRepository));
        _chunkRepository = chunkRepository ?? throw new ArgumentNullException(nameof(chunkRepository));
        _sourceAccessTokenService = sourceAccessTokenService ?? throw new ArgumentNullException(nameof(sourceAccessTokenService));
        _ingestionJobService = ingestionJobService ?? throw new ArgumentNullException(nameof(ingestionJobService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Download an ingestion source file for local processor M2M access.
    /// Route: GET /api/ingestion/artifacts/source
    /// </summary>
    [HttpGet("artifacts/source")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<IActionResult> DownloadSourceAsync(
        [FromQuery] string uploadId = "",
        [FromQuery] string documentType = "manual-pdf",
        CancellationToken ct = default) =>
        DownloadSourceInternalAsync(uploadId, documentType, ct);

    /// <summary>
    /// Download an ingestion source file using a short-lived access token issued at job start.
    /// Route: GET /api/ingestion/artifacts/source/access
    /// </summary>
    [HttpGet("artifacts/source/access")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadSourceWithAccessTokenAsync(
        [FromQuery] string uploadId = "",
        [FromQuery] string documentType = "manual-pdf",
        [FromQuery] string accessToken = "",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken)
            || !_sourceAccessTokenService.IsValid(accessToken, uploadId, documentType))
        {
            return Unauthorized(new ProblemDetails
            {
                Title = "Invalid access token",
                Detail = "The source access token is missing, expired, or does not match the upload.",
                Status = StatusCodes.Status401Unauthorized
            });
        }

        return await DownloadSourceInternalAsync(uploadId, documentType, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Accept a processor artifact (chunks.jsonl or entities.json) and store it in blob storage.
    /// Route: POST /api/ingestion/artifacts/upload
    /// </summary>
    /// <param name="file">The artifact file to upload (multipart/form-data).</param>
    /// <param name="uploadId">Upload session identifier (GUID format).</param>
    /// <param name="artifactType">Artifact type: "search-chunks" or "graph-entities" (case-insensitive).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>202 Accepted with <see cref="ProcessorArtifactUploadResponse"/>.</returns>
    [HttpPost("artifacts/upload")]
    [ProducesResponseType(typeof(ProcessorArtifactUploadResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    [RequestSizeLimit(MaxFileSizeBytes)]
    public async Task<IActionResult> UploadArtifactAsync(
        IFormFile? file,
        [FromQuery] string uploadId = "",
        [FromQuery] string artifactType = "",
        CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "File is required",
                Detail = "A non-empty file must be provided.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (file.Length > MaxFileSizeBytes)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "File too large",
                Detail = "The uploaded file exceeds the maximum allowed size of 500 MB.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (string.IsNullOrWhiteSpace(uploadId) || !Guid.TryParse(uploadId, out _))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid uploadId",
                Detail = "uploadId must be a valid GUID format.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (!IsValidArtifactType(artifactType, out var container, out var blobPath))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid artifactType",
                Detail = "artifactType must be 'search-chunks' or 'graph-entities'.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        // For graph-entities, we need to use the raw-uploads container, not the graph-entities container
        if (string.Equals(artifactType, "graph-entities", StringComparison.OrdinalIgnoreCase))
        {
            container = _blobStorageOptions.RawUploadsContainer;
        }

        blobPath = BuildBlobPath(uploadId, artifactType);

        // Buffer the stream since IFormFile.OpenReadStream() is forward-only
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct).ConfigureAwait(false);
        buffer.Position = 0;

        try
        {
            await _blobStorageService.UploadAsync(
                container,
                blobPath,
                buffer,
                file.ContentType ?? "application/octet-stream",
                ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Processor artifact accepted. UploadId={UploadId}, ArtifactType={ArtifactType}, SizeBytes={SizeBytes}.",
                LogSanitizer.Sanitize(uploadId),
                LogSanitizer.Sanitize(artifactType),
                file.Length);

            // Index search-chunks into Azure AI Search (fire-and-handle: indexing failure does not affect 202 response)
            if (string.Equals(artifactType, "search-chunks", StringComparison.OrdinalIgnoreCase))
            {
                buffer.Position = 0;
                await ProcessSearchChunksAsync(uploadId, container, blobPath, buffer, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to upload processor artifact. UploadId={UploadId}, ArtifactType={ArtifactType}.",
                LogSanitizer.Sanitize(uploadId),
                LogSanitizer.Sanitize(artifactType));

            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Upload failed",
                Detail = "The processor artifact could not be stored.",
                Status = StatusCodes.Status500InternalServerError
            });
        }

        var response = new ProcessorArtifactUploadResponse
        {
            UploadId = uploadId,
            ArtifactType = artifactType,
            BlobPath = blobPath,
            Status = "stored"
        };

        return Accepted(response);
    }

    private async Task ProcessSearchChunksAsync(
        string uploadId,
        string container,
        string blobPath,
        Stream buffer,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var now = DateTimeOffset.UtcNow;

            var result = await _chunkIndexingService.IndexFromJsonlAsync(buffer, uploadId, ct).ConfigureAwait(false);

            var expected = result.TotalParsed;
            var indexed = result.Outcomes.Count(x => x.Succeeded);
            var failed = result.Outcomes.Count(x => !x.Succeeded);

            var artifactState = ComputeArtifactState(indexed, expected);

            var job = await FindLatestSearchChunkJobAsync(uploadId, ct).ConfigureAwait(false);

            if (job is null)
            {
                _logger.LogWarning(
                    "No ingestion job found for uploadId {UploadId}. Skipping catalog write. Artifact is stored in blob.",
                    LogSanitizer.Sanitize(uploadId));
            }
            else
            {
                var artifact = new IndexedArtifact
                {
                    IndexedArtifactId = Guid.NewGuid(),
                    IngestionJobId = job.IngestionJobId,
                    UploadId = uploadId,
                    ArtifactType = "search-chunks",
                    BlobContainer = container,
                    BlobPath = blobPath,
                    SourceFileName = null,
                    State = artifactState,
                    ExpectedChunkCount = expected,
                    IndexedChunkCount = indexed,
                    FailedChunkCount = failed,
                    LastProcessedAtUtc = now,
                    FailureReason = failed > 0 ? $"Failed to index {failed} chunk(s)" : null,
                    CreatedAtUtc = now
                };

                await _artifactRepository.UpsertAsync(artifact, ct).ConfigureAwait(false);

                await _chunkRepository.DeleteByArtifactIdAsync(artifact.IndexedArtifactId, ct).ConfigureAwait(false);

                var indexedChunks = ConvertToIndexedChunks(result.Outcomes, artifact.IndexedArtifactId, job.IngestionJobId, uploadId, now);
                if (indexedChunks.Count > 0)
                {
                    await _chunkRepository.UpsertManyAsync(indexedChunks, ct).ConfigureAwait(false);
                }

                var failureReason = failed > 0 ? $"Failed to index {failed} chunk(s)" : null;
                var terminalStatus = MapArtifactStateToJobStatus(artifactState);
                var transitioned = await TryTransitionSearchChunkJobToTerminalAsync(
                    job.IngestionJobId,
                    terminalStatus,
                    expected,
                    indexed,
                    failureReason,
                    ct).ConfigureAwait(false);

                // T10: structured terminal transition outcome — JobId, result, totals, duration,
                // and failure reason so the run is diagnosable from logs (never silent).
                _logger.LogInformation(
                    "Search chunk indexing terminal outcome: JobId={JobId}, Outcome={Outcome}, Transitioned={Transitioned}, Expected={ExpectedCount}, Indexed={IndexedCount}, Failed={FailedCount}, Batches={BatchCount}, DurationMs={DurationMs}, FailureReason={FailureReason}.",
                    job.IngestionJobId,
                    terminalStatus.ToString(),
                    transitioned,
                    expected,
                    indexed,
                    failed,
                    result.BatchCount,
                    stopwatch.ElapsedMilliseconds,
                    failureReason is null ? string.Empty : LogSanitizer.Sanitize(failureReason));

                if (!transitioned)
                {
                    _logger.LogInformation(
                        "Search chunk artifact for upload {UploadId} was indexed, but ingestion job {JobId} was not in an active transition state.",
                        LogSanitizer.Sanitize(uploadId),
                        job.IngestionJobId);
                }
            }

            try
            {
                await _blobStorageService.SetMetadataAsync(
                    container,
                    blobPath,
                    new Dictionary<string, string>
                    {
                        { "state", artifactState.ToString() },
                        { "dateLastProcessed", now.UtcDateTime.ToString("O") }
                    },
                    ct).ConfigureAwait(false);
            }
            catch (Exception metadataEx)
            {
                _logger.LogError(
                    metadataEx,
                    "Best-effort blob metadata update failed for {UploadId}. Continuing.",
                    LogSanitizer.Sanitize(uploadId));
            }

            _logger.LogInformation(
                "Indexed {IndexedCount} of {ExpectedCount} chunks for upload {UploadId}. State={State}.",
                indexed,
                expected,
                LogSanitizer.Sanitize(uploadId),
                artifactState);
        }
        catch (Exception indexEx)
        {
            stopwatch.Stop();
            _logger.LogError(
                indexEx,
                "Chunk indexing into Azure AI Search failed for upload {UploadId} after {DurationMs}ms. Artifact is stored in blob.",
                LogSanitizer.Sanitize(uploadId),
                stopwatch.ElapsedMilliseconds);

            await TryFailSearchChunkJobAsync(uploadId, indexEx, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Best-effort transition of the search-chunk job for <paramref name="uploadId"/> to
    /// <see cref="IngestionJobStatus.Failed"/> with a precise reason derived from
    /// <paramref name="failure"/> (T6). Failures during this transition are logged but never
    /// rethrown — the 202 response contract (D5) is preserved in all cases.
    /// </summary>
    private async Task TryFailSearchChunkJobAsync(string uploadId, Exception failure, CancellationToken ct)
    {
        try
        {
            var job = await FindLatestSearchChunkJobAsync(uploadId, ct).ConfigureAwait(false);
            if (job is null)
            {
                _logger.LogWarning(
                    "No ingestion job found for uploadId {UploadId} to transition to Failed after indexing failure.",
                    LogSanitizer.Sanitize(uploadId));
                return;
            }

            var reason = BuildIndexingFailureReason(failure);
            var transitioned = await TryTransitionSearchChunkJobToTerminalAsync(
                job.IngestionJobId,
                IngestionJobStatus.Failed,
                expectedChunkCount: 0,
                indexedChunkCount: 0,
                failureReason: reason,
                ct).ConfigureAwait(false);

            if (transitioned)
            {
                _logger.LogInformation(
                    "Transitioned ingestion job {JobId} to Failed after indexing failure for upload {UploadId}.",
                    job.IngestionJobId,
                    LogSanitizer.Sanitize(uploadId));
            }
            else
            {
                _logger.LogWarning(
                    "Ingestion job {JobId} was not in an active state and could not be transitioned to Failed for upload {UploadId}.",
                    job.IngestionJobId,
                    LogSanitizer.Sanitize(uploadId));
            }
        }
        catch (Exception transitionEx)
        {
            // The 202 contract is preserved; job-transition failures must not propagate.
            _logger.LogError(
                transitionEx,
                "Failed to transition ingestion job to Failed for upload {UploadId}. The artifact is stored in blob.",
                LogSanitizer.Sanitize(uploadId));
        }
    }

    /// <summary>
    /// Builds a precise, length-bounded failure reason from an indexing exception for the
    /// job record. Includes the exception type name and message; for Azure SDK failures the
    /// message already contains the HTTP status code.
    /// </summary>
    private static string BuildIndexingFailureReason(Exception failure)
    {
        var message = failure.Message.Trim();
        // Cap length to keep the job record column from overflowing.
        const int maxReasonLength = 500;
        if (message.Length > maxReasonLength)
        {
            message = message[..maxReasonLength];
        }

        return $"{failure.GetType().Name}: {message}";
    }

    private async Task<IngestionJob?> FindLatestSearchChunkJobAsync(
        string uploadId,
        CancellationToken ct)
    {
        var candidates = new List<IngestionJob>();
        var expectedTypes = new[]
        {
            IngestionJobType.PDFManual,
            IngestionJobType.StructuredSpecification,
            IngestionJobType.Batch
        };

        foreach (var inputType in expectedTypes)
        {
            var candidate = await _jobRepository.GetLatestByInputAsync(uploadId, inputType, ct).ConfigureAwait(false);
            if (candidate is not null)
            {
                candidates.Add(candidate);
            }
        }

        return candidates
            .OrderByDescending(static job => job.CreatedAtUtc)
            .FirstOrDefault();
    }

    private async Task<bool> TryTransitionSearchChunkJobToTerminalAsync(
        Guid jobId,
        IngestionJobStatus terminalStatus,
        int expectedChunkCount,
        int indexedChunkCount,
        string? failureReason,
        CancellationToken ct)
    {
        var activeStatuses = new[]
        {
            IngestionJobStatus.Indexing,
            IngestionJobStatus.Processing,
            IngestionJobStatus.Queued
        };

        foreach (var activeStatus in activeStatuses)
        {
            var transitioned = await _jobRepository.TryTransitionToTerminalAsync(
                jobId,
                activeStatus,
                terminalStatus,
                expectedChunkCount,
                indexedChunkCount,
                failureReason,
                ct).ConfigureAwait(false);

            if (transitioned)
            {
                return true;
            }
        }

        return false;
    }

    private static IndexedArtifactState ComputeArtifactState(int indexed, int expected)
    {
        if (indexed == expected && expected > 0)
            return IndexedArtifactState.Completed;
        if (indexed > 0 && indexed < expected)
            return IndexedArtifactState.PartiallyIndexed;
        if (indexed == 0)
            return IndexedArtifactState.Failed;
        return IndexedArtifactState.Pending;
    }

    private static IngestionJobStatus MapArtifactStateToJobStatus(IndexedArtifactState state)
    {
        return state switch
        {
            IndexedArtifactState.Completed => IngestionJobStatus.Completed,
            IndexedArtifactState.PartiallyIndexed => IngestionJobStatus.PartiallyCompleted,
            IndexedArtifactState.Failed => IngestionJobStatus.Failed,
            _ => IngestionJobStatus.Queued
        };
    }

    private static IReadOnlyList<IndexedChunk> ConvertToIndexedChunks(
        IReadOnlyList<ChunkIndexOutcome> outcomes,
        Guid artifactId,
        Guid jobId,
        string uploadId,
        DateTimeOffset now)
    {
        return outcomes.Select(outcome => new IndexedChunk
        {
            ChunkId = outcome.ChunkId,
            IndexedArtifactId = artifactId,
            IngestionJobId = jobId,
            UploadId = uploadId,
            SourceFileName = outcome.SourceFile,
            PageNumber = outcome.PageNumber,
            ChunkIndex = outcome.ChunkIndex,
            Stage = "index",
            Status = outcome.Succeeded ? ChunkIndexStatus.Complete : ChunkIndexStatus.Failed,
            ProcessedAtUtc = now,
            FailureReason = outcome.FailureReason
        }).ToList();
    }

    /// <summary>
    /// Validates the artifact type and returns the corresponding container name.
    /// </summary>
    private static bool IsValidArtifactType(string artifactType, out string container, out string blobPath)
    {
        container = string.Empty;
        blobPath = string.Empty;

        if (string.IsNullOrWhiteSpace(artifactType))
        {
            return false;
        }

        if (string.Equals(artifactType, "search-chunks", StringComparison.OrdinalIgnoreCase))
        {
            container = "search-chunks";
            return true;
        }

        if (string.Equals(artifactType, "graph-entities", StringComparison.OrdinalIgnoreCase))
        {
            container = "graph-entities";
            return true;
        }

        return false;
    }

    private async Task<IActionResult> DownloadSourceInternalAsync(
        string uploadId,
        string documentType,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(uploadId) || !Guid.TryParse(uploadId, out _))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid uploadId",
                Detail = "uploadId must be a valid GUID format.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (!IsAllowedSourceDocumentType(documentType, out var contentType))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid documentType",
                Detail = "documentType must be 'manual-pdf' or 'spec-dataset'.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var blobName = IngestionBlobPaths.BuildRawUploadBlobName(uploadId, documentType);
        var container = _blobStorageOptions.RawUploadsContainer;

        if (!await _blobStorageService.ExistsAsync(container, blobName, ct).ConfigureAwait(false))
        {
            return NotFound(new ProblemDetails
            {
                Title = "Source not found",
                Detail = "The requested ingestion source could not be found.",
                Status = StatusCodes.Status404NotFound
            });
        }

        try
        {
            var stream = await _blobStorageService.DownloadAsync(container, blobName, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "Serving ingestion source. UploadId={UploadId}, DocumentType={DocumentType}.",
                LogSanitizer.Sanitize(uploadId),
                LogSanitizer.Sanitize(documentType));
            return File(stream, contentType);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to download ingestion source. UploadId={UploadId}, DocumentType={DocumentType}.",
                LogSanitizer.Sanitize(uploadId),
                LogSanitizer.Sanitize(documentType));

            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Source download failed",
                Detail = "The ingestion source could not be downloaded.",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    private static bool IsAllowedSourceDocumentType(string documentType, out string contentType)
    {
        contentType = string.Empty;
        if (string.Equals(documentType, "manual-pdf", StringComparison.OrdinalIgnoreCase))
        {
            contentType = "application/pdf";
            return true;
        }

        if (string.Equals(documentType, "spec-dataset", StringComparison.OrdinalIgnoreCase))
        {
            contentType = "text/csv";
            return true;
        }

        return false;
    }

    /// <summary>
    /// Builds the blob path based on artifact type and upload ID.
    /// </summary>
    private static string BuildBlobPath(string uploadId, string artifactType)
    {
        return string.Equals(artifactType, "search-chunks", StringComparison.OrdinalIgnoreCase)
            ? $"{uploadId}/chunks.jsonl"
            : $"graph-entities/{uploadId}/entities.json";
    }

    /// <summary>
    /// Report the current pipeline stage from the local processor.
    /// The processor's job_id is matched to DocIngestionRunId on the IngestionJob.
    /// Route: PATCH /api/ingestion/jobs/by-run/{runId}/status
    /// </summary>
    [HttpPatch("jobs/by-run/{runId}/status")]
    [ProducesResponseType(typeof(IngestionJobStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ReportJobStageByRunIdAsync(
        string runId,
        [FromBody] IngestionJobStageRequest? request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Stage))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid request",
                Detail = "Stage is required.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (string.IsNullOrWhiteSpace(runId))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid runId",
                Detail = "runId is required.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var job = await _jobRepository.GetByDocIngestionRunIdAsync(runId, ct).ConfigureAwait(false);
        if (job is null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Ingestion job not found",
                Detail = $"No ingestion job found for processor job '{runId}'.",
                Status = StatusCodes.Status404NotFound
            });
        }

        var result = await _ingestionJobService.TransitionStageAsync(job.IngestionJobId, request, ct).ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>
    /// Report the current pipeline stage from the local processor.
    /// Route: PATCH /api/ingestion/jobs/{jobId}/status
    /// </summary>
    [HttpPatch("jobs/{jobId:guid}/status")]
    [ProducesResponseType(typeof(IngestionJobStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ReportJobStageAsync(
        Guid jobId,
        [FromBody] IngestionJobStageRequest? request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Stage))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid request",
                Detail = "Stage is required.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        try
        {
            var result = await _ingestionJobService.TransitionStageAsync(jobId, request, ct).ConfigureAwait(false);
            return Ok(result);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return NotFound(new ProblemDetails
            {
                Title = "Ingestion job not found",
                Detail = ex.Message,
                Status = StatusCodes.Status404NotFound
            });
        }
    }
}
