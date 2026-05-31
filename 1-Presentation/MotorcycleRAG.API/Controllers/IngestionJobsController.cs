using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Application.Services.Ingestion;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Application.Features.Ingestion.Validators;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Core.Utilities;

namespace MotorcycleRAG.API.Controllers;

/// <summary>
/// Manages ingestion pipeline jobs: upload, start, and status polling.
/// </summary>
/// <remarks>
/// All endpoints require the <c>mcr-api-admin</c> policy (admin scope + role + client isolation).
/// File names and user-provided paths are never logged — only opaque identifiers (uploadId, jobId).
/// </remarks>
[ApiController]
[Route("api/ingestion")]
[Produces("application/json")]
[Authorize(Policy = "mcr-api-admin")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "Controllers must be public for discovery")]
[EnableRateLimiting("ingestion-jobs")]
public sealed class IngestionJobsController : ControllerBase {
    private const string PdfSourceFileName = "source.pdf";
    private readonly IIngestionJobService _ingestionJobService;
    private readonly IngestionJobValidator _validator;
    private readonly IBlobStorageService _blobStorageService;
    private readonly BlobStorageOptions _blobStorageOptions;
    private readonly IChunkReprocessService _reprocessService;
    private readonly ILogger<IngestionJobsController> _logger;
    /// <summary>Maximum upload size in bytes (2 GB).</summary>
    private const long MaxFileSizeBytes = 2L * 1024 * 1024 * 1024;

    public IngestionJobsController(
        IIngestionJobService ingestionJobService,
        IngestionJobValidator validator,
        IBlobStorageService blobStorageService,
        IOptions<BlobStorageOptions> blobStorageOptions,
        IChunkReprocessService reprocessService,
        ILogger<IngestionJobsController> logger) {
        _ingestionJobService = ingestionJobService ?? throw new ArgumentNullException(nameof(ingestionJobService));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _blobStorageService = blobStorageService ?? throw new ArgumentNullException(nameof(blobStorageService));
        _blobStorageOptions = blobStorageOptions?.Value ?? throw new ArgumentNullException(nameof(blobStorageOptions));
        _reprocessService = reprocessService ?? throw new ArgumentNullException(nameof(reprocessService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Accept a file upload and return an opaque upload identifier for use in job creation.
    /// Route: POST /api/ingestion/jobs/upload
    /// </summary>
    /// <param name="file">The file to upload (multipart/form-data).</param>
    /// <param name="documentType">Document type hint (default: "manual-pdf").</param>
    /// <returns>202 Accepted with <see cref="IngestionUploadResponse"/>.</returns>
    // SECURITY: Never log the file name — log only the uploadId.
    [HttpPost("jobs/upload")]
    [ProducesResponseType(typeof(IngestionUploadResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    [RequestSizeLimit(MaxFileSizeBytes)]
    public async Task<IActionResult> UploadAsync(
        IFormFile? file,
        [FromQuery] string documentType = "manual-pdf",
        CancellationToken ct = default) {
        if (file is null || file.Length == 0) {
            return BadRequest(new ProblemDetails {
                Title = "File is required",
                Detail = "A non-empty file must be provided.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (file.Length > MaxFileSizeBytes) {
            return BadRequest(new ProblemDetails {
                Title = "File too large",
                Detail = "The uploaded file exceeds the maximum allowed size of 2 GB.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (!IsAllowedDocumentType(documentType)) {
            return BadRequest(new ProblemDetails {
                Title = "Invalid document type",
                Detail = "documentType must be 'manual-pdf', 'spec-dataset', or 'bike-graph'.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (!HasExpectedExtension(file.FileName, documentType)) {
            return BadRequest(new ProblemDetails {
                Title = "Invalid file type",
                Detail = $"documentType '{documentType}' requires a {GetExpectedExtension(documentType)} file.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var uploadId = Guid.NewGuid().ToString();
        var blobName = BuildBlobName(uploadId, documentType);

        try {
            await using var stream = file.OpenReadStream();
            await _blobStorageService.UploadAsync(
                _blobStorageOptions.RawUploadsContainer,
                blobName,
                stream,
                GetContentType(file.ContentType, documentType),
                ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Upload accepted. UploadId={UploadId}, DocumentType={DocumentType}, SizeBytes={SizeBytes}.",
                LogSanitizer.Sanitize(uploadId),
                LogSanitizer.Sanitize(documentType),
                file.Length);
        }
        catch (Exception ex) {
            _logger.LogError(
                ex,
                "Failed to upload ingestion source. UploadId={UploadId}, DocumentType={DocumentType}.",
                LogSanitizer.Sanitize(uploadId),
                LogSanitizer.Sanitize(documentType));

            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails {
                Title = "Upload failed",
                Detail = "The ingestion source could not be stored.",
                Status = StatusCodes.Status500InternalServerError
            });
        }

        var response = new IngestionUploadResponse {
            UploadId = uploadId,
            FileName = Path.GetFileName(file.FileName),
            DocumentType = documentType,
            Status = "uploaded"
        };

        return Accepted(response);
    }

    /// <summary>
    /// Start a new ingestion job referencing a previously uploaded file.
    /// Route: POST /api/ingestion/jobs
    /// </summary>
    /// <param name="request">Job start request containing the uploadId and document type.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>202 Accepted with <see cref="IngestionJobStatusResponse"/>.</returns>
    [HttpPost("jobs")]
    [ProducesResponseType(typeof(IngestionJobStatusResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> StartJobAsync(
        [FromBody] IngestionJobStartRequest? request,
        CancellationToken ct) {
        if (request is null) {
            return BadRequest(new ProblemDetails {
                Title = "Request body is required",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var validationErrors = _validator.Validate(request);
        if (validationErrors.Count > 0) {
            return BadRequest(new ProblemDetails {
                Title = "Validation failed",
                Detail = string.Join(" ", validationErrors),
                Status = StatusCodes.Status400BadRequest
            });
        }

        var userId = User.FindFirst("sub")?.Value ?? "unknown";

        _logger.LogInformation(
            "Starting ingestion job for UploadId={UploadId}, DocumentType={DocumentType}.",
            LogSanitizer.Sanitize(request.UploadId),
            LogSanitizer.Sanitize(request.DocumentType));

        var result = await _ingestionJobService.StartJobAsync(request, userId, ct).ConfigureAwait(false);
        return Accepted(result);
    }

    /// <summary>
    /// Imports graph entities that were already produced by the local processor and uploaded to blob storage.
    /// Route: POST /api/ingestion/jobs/graph-import
    /// </summary>
    [HttpPost("jobs/graph-import")]
    [ProducesResponseType(typeof(IngestionJobStatusResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ImportGraphAsync(
        [FromBody] GraphImportStartRequest? request,
        CancellationToken ct) {
        if (request is null || string.IsNullOrWhiteSpace(request.UploadId)) {
            return BadRequest(new ProblemDetails {
                Title = "uploadId is required",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var userId = User.FindFirst("sub")?.Value ?? "unknown";
        var result = await _ingestionJobService.ImportGraphArtifactsAsync(request, userId, ct).ConfigureAwait(false);
        return Accepted(result);
    }

    /// <summary>
    /// Lists recent ingestion jobs for the admin status view.
    /// Route: GET /api/ingestion/jobs?top=50
    /// </summary>
    [HttpGet("jobs")]
    [ProducesResponseType(typeof(IReadOnlyList<IngestionJobStatusResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<IReadOnlyList<IngestionJobStatusResponse>>> GetRecentJobsAsync(
        [FromQuery] int top = 50,
        CancellationToken ct = default) {
        if (top <= 0 || top > 200) {
            return BadRequest(new ProblemDetails {
                Title = "Invalid top value",
                Detail = "top must be between 1 and 200.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        try {
            var result = await _ingestionJobService.GetRecentIngestionJobsAsync(top, ct).ConfigureAwait(false);
            return Accepted((Uri?)null, result);
        }
        catch (InvalidOperationException ex) {
            _logger.LogError(ex, "Failed to load recent ingestion jobs.");
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails {
                Title = "Failed to load ingestion jobs",
                Detail = "The server could not load recent ingestion jobs.",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Lists blob-backed source files that have not completed ingestion yet.
    /// Route: GET /api/ingestion/jobs/pending-files
    /// </summary>
    [HttpGet("jobs/pending-files")]
    [ProducesResponseType(typeof(IReadOnlyList<PendingStorageFileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<IReadOnlyList<PendingStorageFileDto>>> GetPendingFilesAsync(
        CancellationToken ct) {
        try {
            var result = await _ingestionJobService.GetPendingStorageFilesAsync(ct).ConfigureAwait(false);
            return Accepted((Uri?)null, result);
        }
        catch (InvalidOperationException ex) {
            _logger.LogError(ex, "Failed to load pending storage files.");
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails {
                Title = "Failed to load pending storage files",
                Detail = "The server could not load pending storage files.",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Deletes every pending storage file and its associated ingestion history.
    /// Route: DELETE /api/ingestion/jobs/pending-files
    /// </summary>
    [HttpDelete("jobs/pending-files")]
    [ProducesResponseType(typeof(IngestionCleanupResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<IngestionCleanupResponse>> ClearPendingFilesAsync(
        CancellationToken ct) {
        var deletedCount = await _ingestionJobService.ClearPendingStorageFilesAsync(ct).ConfigureAwait(false);
        return Ok(new IngestionCleanupResponse {
            Scope = "pending-files",
            DeletedCount = deletedCount
        });
    }

    /// <summary>
    /// Deletes a single pending storage file and its associated ingestion history.
    /// Route: DELETE /api/ingestion/jobs/pending-files/{uploadId}
    /// </summary>
    [HttpDelete("jobs/pending-files/{uploadId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeletePendingFileAsync(
        string uploadId,
        [FromQuery] string? documentType,
        CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(uploadId)) {
            return BadRequest(new ProblemDetails {
                Title = "uploadId is required",
                Status = StatusCodes.Status400BadRequest
            });
        }

        await _ingestionJobService.DeletePendingStorageFileAsync(
            uploadId,
            documentType ?? string.Empty,
            ct).ConfigureAwait(false);
        return NoContent();
    }

    /// <summary>
    /// Get the status of an ingestion job.
    /// Route: GET /api/ingestion/jobs/{jobId}
    /// </summary>
    /// <param name="jobId">The unique identifier of the ingestion job.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with <see cref="IngestionJobStatusResponse"/> or 404 Not Found.</returns>
    [HttpGet("jobs/{jobId:guid}")]
    [ProducesResponseType(typeof(IngestionJobStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetJobStatusAsync(
        Guid jobId,
        CancellationToken ct) {
        var userId = User.FindFirst("sub")?.Value ?? "unknown";

        var result = await _ingestionJobService.GetJobStatusAsync(jobId, userId, ct).ConfigureAwait(false);
        if (result is null) {
            return NotFound(new ProblemDetails {
                Title = "Ingestion job not found",
                Detail = $"No ingestion job with ID '{jobId}' was found.",
                Status = StatusCodes.Status404NotFound
            });
        }

        return Ok(result);
    }

    /// <summary>
    /// Deletes a single terminal ingestion job.
    /// Route: DELETE /api/ingestion/jobs/{jobId}
    /// </summary>
    [HttpDelete("jobs/{jobId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteJobAsync(
        Guid jobId,
        CancellationToken ct) {
        var userId = User.FindFirst("sub")?.Value ?? "unknown";
        var job = await _ingestionJobService.GetJobStatusAsync(jobId, userId, ct).ConfigureAwait(false);
        if (job is null) {
            return NotFound(new ProblemDetails {
                Title = "Ingestion job not found",
                Detail = $"No ingestion job with ID '{jobId}' was found.",
                Status = StatusCodes.Status404NotFound
            });
        }

        if (!IsTerminalJobStatus(job.Status)) {
            return Conflict(new ProblemDetails {
                Title = "Only terminal jobs can be deleted",
                Detail = "Queued, processing, and indexing jobs cannot be deleted.",
                Status = StatusCodes.Status409Conflict
            });
        }

        try {
            await _ingestionJobService.DeleteJobAsync(jobId, userId, ct).ConfigureAwait(false);
            return NoContent();
        }
        catch (InvalidOperationException ex) {
            _logger.LogWarning(ex, "Deletion rejected for ingestion job {JobId}.", jobId);
            return Conflict(new ProblemDetails {
                Title = "Job deletion rejected",
                Detail = "The selected ingestion job could not be deleted.",
                Status = StatusCodes.Status409Conflict
            });
        }
    }

    /// <summary>
    /// Deletes all failed and cancelled ingestion jobs.
    /// Route: DELETE /api/ingestion/jobs/failed
    /// </summary>
    [HttpDelete("jobs/failed")]
    [ProducesResponseType(typeof(IngestionCleanupResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<IngestionCleanupResponse>> ClearFailedJobsAsync(
        CancellationToken ct) {
        var userId = User.FindFirst("sub")?.Value ?? "unknown";
        var deletedCount = await _ingestionJobService.ClearFailedJobsAsync(userId, ct).ConfigureAwait(false);
        return Ok(new IngestionCleanupResponse {
            Scope = "failed-jobs",
            DeletedCount = deletedCount
        });
    }

    /// <summary>
    /// Deletes all completed and partially completed ingestion jobs and their source artifacts.
    /// Route: DELETE /api/ingestion/jobs/finished
    /// </summary>
    [HttpDelete("jobs/finished")]
    [ProducesResponseType(typeof(IngestionCleanupResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<IngestionCleanupResponse>> ClearFinishedJobsAsync(
        CancellationToken ct) {
        var userId = User.FindFirst("sub")?.Value ?? "unknown";
        var deletedCount = await _ingestionJobService.ClearFinishedJobsAsync(userId, ct).ConfigureAwait(false);
        return Ok(new IngestionCleanupResponse {
            Scope = "finished-jobs",
            DeletedCount = deletedCount
        });
    }

    /// <summary>
    /// Retries a failed or cancelled ingestion job.
    /// Route: POST /api/ingestion/jobs/{jobId}/retry
    /// </summary>
    [HttpPost("jobs/{jobId:guid}/retry")]
    [ProducesResponseType(typeof(IngestionJobStatusResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RetryJobAsync(
        Guid jobId,
        CancellationToken ct) {
        var userId = User.FindFirst("sub")?.Value ?? "unknown";
        var job = await _ingestionJobService.GetJobStatusAsync(jobId, userId, ct).ConfigureAwait(false);
        if (job is null) {
            return NotFound(new ProblemDetails {
                Title = "Ingestion job not found",
                Detail = $"No ingestion job with ID '{jobId}' was found.",
                Status = StatusCodes.Status404NotFound
            });
        }

        if (!IsFailedJobStatus(job.Status)) {
            return Conflict(new ProblemDetails {
                Title = "Only failed jobs can be retried",
                Detail = "Completed, partially completed, queued, processing, and indexing jobs cannot be retried.",
                Status = StatusCodes.Status409Conflict
            });
        }

        try {
            var result = await _ingestionJobService.RetryJobAsync(jobId, userId, ct).ConfigureAwait(false);
            return Accepted(result);
        }
        catch (InvalidOperationException ex) {
            _logger.LogWarning(ex, "Retry rejected for ingestion job {JobId}.", jobId);
            return Conflict(new ProblemDetails {
                Title = "Job retry rejected",
                Detail = "The selected ingestion job could not be retried.",
                Status = StatusCodes.Status409Conflict
            });
        }
    }

    /// <summary>
    /// Reprocess a specific ingestion job.
    /// Route: POST /api/ingestion/jobs/{jobId}/reprocess
    /// </summary>
    [HttpPost("jobs/{jobId:guid}/reprocess")]
    [ProducesResponseType(typeof(ReprocessResultDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReprocessJobAsync(
        Guid jobId,
        CancellationToken ct)
    {
        _logger.LogInformation("Reprocessing ingestion job {JobId}.", jobId);

        try
        {
            var result = await _reprocessService.ReprocessByJobIdAsync(jobId, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Reprocess job {JobId}: {Processed} processed, {Succeeded} succeeded, {PartiallyIndexed} partially indexed, {Failed} failed.",
                jobId,
                result.ArtifactsProcessed,
                result.ArtifactsSucceeded,
                result.ArtifactsPartiallyIndexed,
                result.ArtifactsFailed);

            return Accepted((Uri?)null, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reprocess operation failed for job {JobId}.", jobId);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Reprocess operation failed",
                Detail = "The reprocess operation could not be completed.",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Reprocess all ingestion jobs that have not succeeded.
    /// Route: POST /api/ingestion/jobs/reprocess/not-succeeded
    /// </summary>
    [HttpPost("jobs/reprocess/not-succeeded")]
    [ProducesResponseType(typeof(ReprocessResultDto), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ReprocessNotSucceededAsync(
        CancellationToken ct)
    {
        _logger.LogInformation("Reprocessing all not-succeeded ingestion jobs.");

        try
        {
            var result = await _reprocessService.ReprocessAllNotSucceededAsync(ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Reprocess not-succeeded: {Processed} processed, {Succeeded} succeeded, {PartiallyIndexed} partially indexed, {Failed} failed.",
                result.ArtifactsProcessed,
                result.ArtifactsSucceeded,
                result.ArtifactsPartiallyIndexed,
                result.ArtifactsFailed);

            return Accepted((Uri?)null, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reprocess not-succeeded operation failed.");
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Reprocess operation failed",
                Detail = "The reprocess operation could not be completed.",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Reprocess all ingestion jobs.
    /// Route: POST /api/ingestion/jobs/reprocess/all
    /// </summary>
    [HttpPost("jobs/reprocess/all")]
    [ProducesResponseType(typeof(ReprocessResultDto), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ReprocessAllAsync(
        CancellationToken ct)
    {
        _logger.LogInformation("Reprocessing all ingestion jobs.");

        try
        {
            var result = await _reprocessService.ReprocessAllAsync(ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Reprocess all: {Processed} processed, {Succeeded} succeeded, {PartiallyIndexed} partially indexed, {Failed} failed.",
                result.ArtifactsProcessed,
                result.ArtifactsSucceeded,
                result.ArtifactsPartiallyIndexed,
                result.ArtifactsFailed);

            return Accepted((Uri?)null, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reprocess all operation failed.");
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Reprocess operation failed",
                Detail = "The reprocess operation could not be completed.",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>Returns true when the document type is on the allowlist.</summary>
    private static bool IsAllowedDocumentType(string documentType) =>
        string.Equals(documentType, "manual-pdf", StringComparison.OrdinalIgnoreCase)
        || string.Equals(documentType, "spec-dataset", StringComparison.OrdinalIgnoreCase)
        || string.Equals(documentType, "bike-graph", StringComparison.OrdinalIgnoreCase);

    private static bool IsTerminalJobStatus(string status) =>
        string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "PartiallyCompleted", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase);

    private static bool IsFailedJobStatus(string status) =>
        string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase);

    private static bool HasExpectedExtension(string fileName, string documentType) =>
        string.Equals(
            Path.GetExtension(fileName),
            GetExpectedExtension(documentType),
            StringComparison.OrdinalIgnoreCase);

    private static string GetExpectedExtension(string documentType) =>
        string.Equals(documentType, "manual-pdf", StringComparison.OrdinalIgnoreCase) ? ".pdf" : ".csv";

    private static string BuildBlobName(string uploadId, string documentType) =>
        string.Equals(documentType, "manual-pdf", StringComparison.OrdinalIgnoreCase)
            ? $"{uploadId}/{PdfSourceFileName}"
            : $"{uploadId}.csv";

    private static string GetContentType(string? incomingContentType, string documentType) {
        if (!string.IsNullOrWhiteSpace(incomingContentType)
            && !string.Equals(incomingContentType, "application/octet-stream", StringComparison.OrdinalIgnoreCase)) {
            return incomingContentType;
        }

        return string.Equals(documentType, "manual-pdf", StringComparison.OrdinalIgnoreCase)
            ? "application/pdf"
            : "text/csv";
    }
}
