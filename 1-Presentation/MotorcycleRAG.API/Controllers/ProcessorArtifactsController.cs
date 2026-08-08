using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.API.Controllers;

/// <summary>
/// Accepts processed artifacts from the Python local processing service via M2M authentication.
/// </summary>
[ApiController]
[Route("api/ingestion")]
[Produces("application/json")]
[Authorize(Policy = "mcr-api-local-processor")]
[EnableRateLimiting("ingestion-jobs")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "Controllers must be public for discovery")]
public sealed class ProcessorArtifactsController : ControllerBase
{
    private const long MaxFileSizeBytes = 500L * 1024 * 1024;
    private readonly IProcessorArtifactService _processorArtifactService;
    private readonly ILogger<ProcessorArtifactsController> _logger;

    public ProcessorArtifactsController(
        IProcessorArtifactService processorArtifactService,
        ILogger<ProcessorArtifactsController> logger)
    {
        _processorArtifactService = processorArtifactService ?? throw new ArgumentNullException(nameof(processorArtifactService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpGet("artifacts/source")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadSourceAsync(
        [FromQuery] string uploadId = "",
        [FromQuery] string documentType = "manual-pdf",
        CancellationToken ct = default) =>
        await MapSourceResultAsync(uploadId, documentType, accessToken: null, ct).ConfigureAwait(false);

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
        CancellationToken ct = default) =>
        await MapSourceResultAsync(uploadId, documentType, accessToken, ct).ConfigureAwait(false);

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
            return BadRequest(Problem(StatusCodes.Status400BadRequest, "File is required", "A non-empty file must be provided."));
        }

        if (file.Length > MaxFileSizeBytes)
        {
            return BadRequest(Problem(StatusCodes.Status400BadRequest, "File too large", "The uploaded file exceeds the maximum allowed size of 500 MB."));
        }

        await using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct).ConfigureAwait(false);
        buffer.Position = 0;

        try
        {
            var result = await _processorArtifactService.UploadArtifactAsync(
                new ProcessorArtifactUploadRequest(uploadId, artifactType, buffer, file.ContentType ?? "application/octet-stream"),
                ct).ConfigureAwait(false);
            return result.Status switch
            {
                ProcessorArtifactOperationStatus.Success => Accepted(result.Response),
                ProcessorArtifactOperationStatus.InvalidUploadId => BadRequest(Problem(StatusCodes.Status400BadRequest, "Invalid uploadId", "uploadId must be a valid GUID format.")),
                ProcessorArtifactOperationStatus.InvalidArtifactType => BadRequest(Problem(StatusCodes.Status400BadRequest, "Invalid artifactType", "artifactType must be 'search-chunks' or 'graph-entities'.")),
                ProcessorArtifactOperationStatus.IndexingSkipped => Accepted(result.Response),
                _ => StatusCode(StatusCodes.Status500InternalServerError, Problem(StatusCodes.Status500InternalServerError, "Upload failed", "The processor artifact could not be stored."))
            };
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to upload processor artifact.");
            return StatusCode(StatusCodes.Status500InternalServerError, Problem(StatusCodes.Status500InternalServerError, "Upload failed", "The processor artifact could not be stored."));
        }
    }

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
            return BadRequest(Problem(StatusCodes.Status400BadRequest, "Invalid request", "Stage is required."));
        }

        if (string.IsNullOrWhiteSpace(runId))
        {
            return BadRequest(Problem(StatusCodes.Status400BadRequest, "Invalid runId", "runId is required."));
        }

        var result = await _processorArtifactService.ReportJobStageByRunIdAsync(runId, request, ct).ConfigureAwait(false);
        return result is null
            ? NotFound(Problem(StatusCodes.Status404NotFound, "Ingestion job not found", $"No ingestion job found for processor job '{runId}'."))
            : Ok(result);
    }

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
            return BadRequest(Problem(StatusCodes.Status400BadRequest, "Invalid request", "Stage is required."));
        }

        try
        {
            return Ok(await _processorArtifactService.ReportJobStageAsync(jobId, request, ct).ConfigureAwait(false));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(Problem(StatusCodes.Status404NotFound, "Ingestion job not found", exception.Message));
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return NotFound(Problem(StatusCodes.Status404NotFound, "Ingestion job not found", exception.Message));
        }
    }

    private async Task<IActionResult> MapSourceResultAsync(string uploadId, string documentType, string? accessToken, CancellationToken ct)
    {
        try
        {
            var result = await _processorArtifactService.DownloadSourceAsync(uploadId, documentType, accessToken, ct).ConfigureAwait(false);
            return result.Status switch
            {
                ProcessorArtifactOperationStatus.Success => File(result.Content!, result.ContentType!),
                ProcessorArtifactOperationStatus.InvalidUploadId => BadRequest(Problem(StatusCodes.Status400BadRequest, "Invalid uploadId", "uploadId must be a valid GUID format.")),
                ProcessorArtifactOperationStatus.InvalidDocumentType => BadRequest(Problem(StatusCodes.Status400BadRequest, "Invalid documentType", "documentType must be 'manual-pdf' or 'spec-dataset'.")),
                ProcessorArtifactOperationStatus.Unauthorized => Unauthorized(Problem(StatusCodes.Status401Unauthorized, "Invalid access token", "The source access token is missing, expired, or does not match the upload.")),
                ProcessorArtifactOperationStatus.NotFound => NotFound(Problem(StatusCodes.Status404NotFound, "Source not found", "The requested ingestion source could not be found.")),
                _ => StatusCode(StatusCodes.Status500InternalServerError, Problem(StatusCodes.Status500InternalServerError, "Source download failed", "The ingestion source could not be downloaded."))
            };
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to download ingestion source.");
            return StatusCode(StatusCodes.Status500InternalServerError, Problem(StatusCodes.Status500InternalServerError, "Source download failed", "The ingestion source could not be downloaded."));
        }
    }

    private static ProblemDetails Problem(int status, string title, string detail) => new()
    {
        Status = status,
        Title = title,
        Detail = detail
    };
}
