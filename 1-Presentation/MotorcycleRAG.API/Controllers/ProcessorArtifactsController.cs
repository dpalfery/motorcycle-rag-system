using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Core.Utilities;

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
    private readonly ILogger<ProcessorArtifactsController> _logger;

    /// <summary>Maximum upload size in bytes (500 MB).</summary>
    private const long MaxFileSizeBytes = 500L * 1024 * 1024;

    public ProcessorArtifactsController(
        IBlobStorageService blobStorageService,
        IOptions<BlobStorageOptions> blobStorageOptions,
        ILogger<ProcessorArtifactsController> logger)
    {
        _blobStorageService = blobStorageService ?? throw new ArgumentNullException(nameof(blobStorageService));
        _blobStorageOptions = blobStorageOptions?.Value ?? throw new ArgumentNullException(nameof(blobStorageOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

        try
        {
            await using var stream = file.OpenReadStream();
            await _blobStorageService.UploadAsync(
                container,
                blobPath,
                stream,
                file.ContentType ?? "application/octet-stream",
                ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Processor artifact accepted. UploadId={UploadId}, ArtifactType={ArtifactType}, SizeBytes={SizeBytes}.",
                LogSanitizer.Sanitize(uploadId),
                LogSanitizer.Sanitize(artifactType),
                file.Length);
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

    /// <summary>
    /// Builds the blob path based on artifact type and upload ID.
    /// </summary>
    private static string BuildBlobPath(string uploadId, string artifactType)
    {
        return string.Equals(artifactType, "search-chunks", StringComparison.OrdinalIgnoreCase)
            ? $"{uploadId}/chunks.jsonl"
            : $"graph-entities/{uploadId}/entities.json";
    }
}
