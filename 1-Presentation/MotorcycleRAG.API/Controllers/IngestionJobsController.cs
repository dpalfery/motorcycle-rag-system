using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MotorcycleRAG.Application.Pipeline;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Application.Pipeline.Validators;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Core.Utilities;
using Microsoft.Extensions.Options;

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
    private readonly ILogger<IngestionJobsController> _logger;
    /// <summary>Maximum upload size in bytes (2 GB).</summary>
    private const long MaxFileSizeBytes = 2L * 1024 * 1024 * 1024;

    public IngestionJobsController(
        IIngestionJobService ingestionJobService,
        IngestionJobValidator validator,
        IBlobStorageService blobStorageService,
        IOptions<BlobStorageOptions> blobStorageOptions,
        ILogger<IngestionJobsController> logger) {
        _ingestionJobService = ingestionJobService ?? throw new ArgumentNullException(nameof(ingestionJobService));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _blobStorageService = blobStorageService ?? throw new ArgumentNullException(nameof(blobStorageService));
        _blobStorageOptions = blobStorageOptions?.Value ?? throw new ArgumentNullException(nameof(blobStorageOptions));
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
            return Ok(result);
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
            return Ok(result);
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

    /// <summary>Returns true when the document type is on the allowlist.</summary>
    private static bool IsAllowedDocumentType(string documentType) =>
        string.Equals(documentType, "manual-pdf", StringComparison.OrdinalIgnoreCase)
        || string.Equals(documentType, "spec-dataset", StringComparison.OrdinalIgnoreCase)
        || string.Equals(documentType, "bike-graph", StringComparison.OrdinalIgnoreCase);

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
