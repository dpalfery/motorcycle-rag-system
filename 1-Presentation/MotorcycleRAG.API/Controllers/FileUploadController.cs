using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Utilities;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using System.Collections.ObjectModel;

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
    private readonly IFileUploadService _fileUploadService;
    private readonly IDataPipelineOrchestrator _orchestrator;
    private readonly ILogger<FileUploadController> _logger;

    public FileUploadController(
        IFileUploadService fileUploadService,
        IDataPipelineOrchestrator orchestrator,
        ILogger<FileUploadController> logger)
    {
        _fileUploadService = fileUploadService ?? throw new ArgumentNullException(nameof(fileUploadService));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
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
    [ProducesResponseType(typeof(ProblemDetails), 500)]
    public async Task<IActionResult> UploadFileAsync(IFormFile file)
    {
        return await UploadFileInternalAsync(file, false);
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
    [ProducesResponseType(typeof(ProblemDetails), 500)]
    public async Task<IActionResult> UploadFileWithProcessingAsync(
        IFormFile file,
        [FromQuery] bool processImmediately)
    {
        return await UploadFileInternalAsync(file, processImmediately);
    }

    /// <summary>
    /// Internal implementation for file upload
    /// </summary>
    private async Task<IActionResult> UploadFileInternalAsync(IFormFile file, bool processImmediately)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("No file provided or file is empty");
        }

        try
        {
            var options = new FileUploadOptions
            {
                ValidateFileContent = true,
                GenerateUniqueFileName = true
            };

            var metadata = new FileMetadata
            {
                FileName = file.FileName,
                ContentType = file.ContentType,
                ContentLength = file.Length
            };

            await using var stream = file.OpenReadStream();
            var uploadResult = await _fileUploadService.UploadFileAsync(stream, metadata, options, HttpContext.RequestAborted);

            if (!uploadResult.IsValid)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "File validation failed",
                    Detail = string.Join(", ", uploadResult.ValidationResult.Errors),
                    Status = 400
                });
            }

            // If immediate processing is requested, process the file
            if (processImmediately)
            {
                var pipelineRequest = new DataPipelineRequest
                {
                    FileName = uploadResult.OriginalFileName,
                    FilePath = uploadResult.FilePath,
                    FileType = uploadResult.DetectedFileType,
                    CreatedBy = "API",
                    Options = new PipelineOptions
                    {
                        IndexImmediately = true,
                        ProcessImages = true,
                        GenerateEmbeddings = true
                    }
                };

                var processingResult = await _orchestrator.ProcessFileAsync(pipelineRequest, HttpContext.RequestAborted);

                return Ok(new FileUploadResponse
                {
                    Upload = uploadResult,
                    Processing = processingResult
                });
            }

            return Ok(uploadResult);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "File upload was cancelled");
            return StatusCode(499, new ProblemDetails
            {
                Title = "Request cancelled",
                Detail = "The file upload was cancelled by the client",
                Status = 499
            });
        }
        catch (Exception ex)
        {
            var fileName = file?.FileName ?? "unknown";
            _logger.LogError(ex, "Error uploading file {FileName}", LogSanitizer.Sanitize(fileName));
            return StatusCode(500, new ProblemDetails
            {
                Title = "Internal server error",
                Detail = "An error occurred while uploading the file",
                Status = 500
            });
        }
    }

    /// <summary>
    /// Upload multiple files for processing (without immediate processing)
    /// </summary>
    /// <param name="files">The files to upload</param>
    /// <returns>Batch file upload result</returns>
    [HttpPost("batch")]
    [ProducesResponseType(typeof(BatchFileUploadResult), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 500)]
    public async Task<IActionResult> UploadFilesAsync(IReadOnlyList<IFormFile> files)
    {
        return await UploadFilesInternalAsync(files, false);
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
    [ProducesResponseType(typeof(ProblemDetails), 500)]
    public async Task<IActionResult> UploadFilesWithProcessingAsync(
        IReadOnlyList<IFormFile> files,
        [FromQuery] bool processImmediately)
    {
        return await UploadFilesInternalAsync(files, processImmediately);
    }

    /// <summary>
    /// Internal implementation for batch file upload
    /// </summary>
    private async Task<IActionResult> UploadFilesInternalAsync(IReadOnlyList<IFormFile> files, bool processImmediately)
    {
        if (files == null || files.Count == 0)
        {
            return BadRequest("No files provided");
        }

        // Enforce batch size limit for DoS mitigation
        var constraints = _fileUploadService.GetUploadConstraints();
        if (files.Count > constraints.MaxFilesPerBatch)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Batch size exceeded",
                Detail = $"Maximum {constraints.MaxFilesPerBatch} files allowed per batch. Requested: {files.Count}",
                Status = 400
            });
        }

        try
        {
            var options = new FileUploadOptions
            {
                ValidateFileContent = true,
                GenerateUniqueFileName = true
            };

            var fileUploads = files.Select(f =>
            {
                var stream = f.OpenReadStream();
                var metadata = new FileMetadata
                {
                    FileName = f.FileName,
                    ContentType = f.ContentType,
                    ContentLength = f.Length
                };
                return (stream, metadata);
            }).ToList();

            var uploadResult = await _fileUploadService.UploadFilesAsync(fileUploads, options, HttpContext.RequestAborted);

            // Dispose streams after upload
            foreach (var (stream, _) in fileUploads)
            {
                await stream.DisposeAsync();
            }

            if (processImmediately && uploadResult.SuccessfulUploads > 0)
            {
                var pipelineRequests = uploadResult.Results
                    .Where(r => r.IsValid)
                    .Select(r => new DataPipelineRequest
                    {
                        FileName = r.OriginalFileName,
                        FilePath = r.FilePath,
                        FileType = r.DetectedFileType,
                        CreatedBy = "API",
                        Options = new PipelineOptions
                        {
                            IndexImmediately = true,
                            ProcessImages = true,
                            GenerateEmbeddings = true
                        }
                    })
                    .ToList();

                var batchResult = await _orchestrator.ProcessBatchAsync(pipelineRequests, HttpContext.RequestAborted);

                return Ok(new BatchFileUploadResponse
                {
                    Upload = uploadResult,
                    Processing = batchResult
                });
            }

            return Ok(uploadResult);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "Batch file upload was cancelled");
            return StatusCode(499, new ProblemDetails
            {
                Title = "Request cancelled",
                Detail = "The batch file upload was cancelled by the client",
                Status = 499
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading batch files");
            return StatusCode(500, new ProblemDetails
            {
                Title = "Internal server error",
                Detail = "An error occurred while uploading files",
                Status = 500
            });
        }
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
            var constraints = _fileUploadService.GetUploadConstraints();
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
}
