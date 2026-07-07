using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.API.Controllers;

/// <summary>
/// Response for file upload operations
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "DTOs must be public for API documentation")]
public class FileUploadResponse
{
    public FileUploadResult Upload { get; set; } = new FileUploadResult();
    public PipelineExecutionResult? Processing { get; set; }
}

/// <summary>
/// Response for batch file upload operations
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "DTOs must be public for API documentation")]
public class BatchFileUploadResponse
{
    public BatchFileUploadResult Upload { get; set; } = new BatchFileUploadResult();
    public BatchPipelineResult? Processing { get; set; }
}

/// <summary>
/// Controller for file upload operations including single and batch file uploads
/// </summary>
[ApiController]
[Route("api/file-upload")]
[Produces("application/json")]
[Authorize(Policy = "mcr-api-admin")] // Require Admin policy for all upload operations
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "Controllers must be public for discovery")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1056:Uri properties should not be strings", Justification = "DTOs for API")]
public class FileUploadController : ControllerBase
{
    private readonly FileUploadConfiguration _fileUploadConfiguration;
    private readonly ILogger<FileUploadController> _logger;

    public FileUploadController(
        IOptions<FileUploadConfiguration> fileUploadConfiguration,
        ILogger<FileUploadController> logger)
    {
        _fileUploadConfiguration = fileUploadConfiguration?.Value ?? throw new ArgumentNullException(nameof(fileUploadConfiguration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Upload a single file for processing (without immediate processing)
    /// </summary>
    /// <param name="file">The file to upload</param>
    /// <returns>File upload result</returns>
    [HttpPost]
    [ProducesResponseType(typeof(FileUploadResult), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status410Gone)]
    [ProducesResponseType(typeof(ProblemDetails), 500)]
    public IActionResult UploadFileAsync(IFormFile file)
    {
        _ = file;
        return LegacyDiskUploadGone();
    }

    /// <summary>
    /// Upload a single file for processing with immediate processing option
    /// </summary>
    /// <param name="file">The file to upload</param>
    /// <param name="processImmediately">Whether to process the file immediately</param>
    /// <returns>File upload result with optional processing result</returns>
    [HttpPost("with-processing")]
    [ProducesResponseType(typeof(FileUploadResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status410Gone)]
    [ProducesResponseType(typeof(ProblemDetails), 500)]
    public IActionResult UploadFileWithProcessingAsync(
        IFormFile file,
        [FromQuery] bool processImmediately)
    {
        _ = file;
        _ = processImmediately;
        return LegacyDiskUploadGone();
    }

    /// <summary>
    /// Upload multiple files for processing (without immediate processing)
    /// </summary>
    /// <param name="files">The files to upload</param>
    /// <returns>Batch file upload result</returns>
    [HttpPost("batch")]
    [ProducesResponseType(typeof(BatchFileUploadResult), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status410Gone)]
    [ProducesResponseType(typeof(ProblemDetails), 500)]
    public IActionResult UploadFilesAsync(IReadOnlyList<IFormFile> files)
    {
        _ = files;
        return LegacyDiskUploadGone();
    }

    /// <summary>
    /// Upload multiple files for processing with immediate processing option
    /// </summary>
    /// <param name="files">The files to upload</param>
    /// <param name="processImmediately">Whether to process the files immediately</param>
    /// <returns>Batch file upload result with optional processing result</returns>
    [HttpPost("batch-with-processing")]
    [ProducesResponseType(typeof(BatchFileUploadResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status410Gone)]
    [ProducesResponseType(typeof(ProblemDetails), 500)]
    public IActionResult UploadFilesWithProcessingAsync(
        IReadOnlyList<IFormFile> files,
        [FromQuery] bool processImmediately)
    {
        _ = files;
        _ = processImmediately;
        return LegacyDiskUploadGone();
    }

    /// <summary>
    /// Get file upload constraints
    /// </summary>
    /// <returns>Upload constraints information</returns>
    [HttpGet("constraints")]
    [ProducesResponseType(typeof(FileUploadConstraints), 200)]
    public IActionResult GetUploadConstraints()
    {
        try
        {
            var constraints = CreateUploadConstraints();
            return Ok(constraints);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting upload constraints");
            return StatusCode(500, new ProblemDetails
            {
                Title = "Internal server error",
                Detail = "An error occurred while retrieving upload constraints",
                Status = 500
            });
        }
    }

    private ObjectResult LegacyDiskUploadGone()
    {
        _logger.LogInformation("Legacy disk upload endpoint rejected with 410 Gone.");

        return StatusCode(StatusCodes.Status410Gone, new ProblemDetails
        {
            Title = "Legacy disk upload is no longer supported",
            Detail = "Use the blob-backed ingestion upload endpoint at /api/ingestion/jobs/upload.",
            Status = StatusCodes.Status410Gone
        });
    }

    private FileUploadConstraints CreateUploadConstraints()
    {
        var constraints = new FileUploadConstraints
        {
            MaxFileSizeBytes = _fileUploadConfiguration.MaxFileSizeBytes,
            MaxFileSizeDisplay = FormatFileSize(_fileUploadConfiguration.MaxFileSizeBytes),
            MaxFilesPerBatch = _fileUploadConfiguration.MaxFilesPerBatch
        };

        constraints.SupportedFileTypes.Add("CSV");
        constraints.SupportedFileTypes.Add("PDF");

        foreach (var extension in _fileUploadConfiguration.AllowedExtensions)
        {
            constraints.SupportedExtensions.Add(extension.ToLowerInvariant());
        }

        constraints.FileTypeDescriptions["CSV"] = "Comma-separated values files for motorcycle specification data";
        constraints.FileTypeDescriptions["PDF"] = "PDF documents for motorcycle manuals";

        return constraints;
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB"];
        var len = (double)bytes;
        var order = 0;

        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }
}
