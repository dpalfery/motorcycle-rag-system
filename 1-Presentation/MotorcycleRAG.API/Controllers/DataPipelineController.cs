using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.DTOs;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MotorcycleRAG.API.Controllers;

/// <summary>
/// Response for cancel pipeline operation
/// </summary>
public class CancelPipelineResponse
{
    public string ExecutionId { get; set; } = string.Empty;
    public bool Cancelled { get; set; }
}

/// <summary>
/// Response for get pipeline status operation
/// </summary>
public class PipelineStatusResponse
{
    public string ExecutionId { get; set; } = string.Empty;
    public PipelineStatus Status { get; set; }
}

/// <summary>
/// Controller for data pipeline operations including file upload and processing
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Authorize(Policy = "DataAdmin")] // Require DataAdmin role for all pipeline operations
public class DataPipelineController : ControllerBase
{
    private readonly IDataPipelineOrchestrator _orchestrator;
    private readonly IFileUploadService _fileUploadService;
    private readonly IScheduledPipelineService _scheduledService;
    private readonly IPipelineMonitoringService _monitoringService;
    private readonly ILogger<DataPipelineController> _logger;

    public DataPipelineController(
        IDataPipelineOrchestrator orchestrator,
        IFileUploadService fileUploadService,
        IScheduledPipelineService scheduledService,
        IPipelineMonitoringService monitoringService,
        ILogger<DataPipelineController> logger)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _fileUploadService = fileUploadService ?? throw new ArgumentNullException(nameof(fileUploadService));
        _scheduledService = scheduledService ?? throw new ArgumentNullException(nameof(scheduledService));
        _monitoringService = monitoringService ?? throw new ArgumentNullException(nameof(monitoringService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Sanitizes user-provided values for logging to prevent log injection attacks.
    /// Replaces newlines, carriage returns, and tabs with spaces.
    /// </summary>
    private string SanitizeForLogging(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        return input
            .Replace('\n', ' ')
            .Replace('\r', ' ')
            .Replace('\t', ' ');
    }

    /// <summary>
    /// Upload a single file for processing
    /// </summary>
    /// <param name="file">The file to upload</param>
    /// <param name="processImmediately">Whether to process the file immediately</param>
    /// <returns>File upload result</returns>
    [HttpPost("upload")]
    [ProducesResponseType(typeof(FileUploadResult), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 500)]
    public async Task<IActionResult> UploadFileAsync(
        IFormFile file,
        [FromQuery] bool processImmediately = false)
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
                
                return Ok(new
                {
                    Upload = uploadResult,
                    Processing = processingResult
                });
            }

            return Ok(uploadResult);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("File upload was cancelled");
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
            _logger.LogError(ex, "Error uploading file {FileName}", SanitizeForLogging(fileName));
            return StatusCode(500, new ProblemDetails
            {
                Title = "Internal server error",
                Detail = "An error occurred while uploading the file",
                Status = 500
            });
        }
    }

    /// <summary>
    /// Upload multiple files for processing
    /// </summary>
    /// <param name="files">The files to upload</param>
    /// <param name="processImmediately">Whether to process the files immediately</param>
    /// <returns>Batch file upload result</returns>
    [HttpPost("upload-batch")]
    [ProducesResponseType(typeof(BatchFileUploadResult), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 500)]
    public async Task<IActionResult> UploadFilesAsync(
        List<IFormFile> files,
        [FromQuery] bool processImmediately = false)
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

                return Ok(new
                {
                    Upload = uploadResult,
                    Processing = batchResult
                });
            }

            return Ok(uploadResult);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Batch file upload was cancelled");
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
    /// Process a file that has already been uploaded
    /// </summary>
    /// <param name="request">Pipeline processing request</param>
    /// <returns>Pipeline execution result</returns>
    [HttpPost("process")]
    [ProducesResponseType(typeof(PipelineExecutionResult), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 500)]
    public async Task<IActionResult> ProcessFileAsync([FromBody] DataPipelineRequest request)
    {
        if (request == null)
        {
            return BadRequest("Request cannot be null");
        }

        if (string.IsNullOrWhiteSpace(request.FilePath) || !System.IO.File.Exists(request.FilePath))
        {
            return BadRequest("File does not exist at the specified path");
        }

        try
        {
            var result = await _orchestrator.ProcessFileAsync(request, HttpContext.RequestAborted);
            return Ok(result);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("File processing was cancelled");
            return StatusCode(499, new ProblemDetails
            {
                Title = "Request cancelled",
                Detail = "The file processing was cancelled by the client",
                Status = 499
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing file {FilePath}", SanitizeForLogging(request.FilePath));
            return StatusCode(500, new ProblemDetails
            {
                Title = "Internal server error",
                Detail = "An error occurred while processing the file",
                Status = 500
            });
        }
    }

    /// <summary>
    /// Process multiple files in batch
    /// </summary>
    /// <param name="requests">List of pipeline processing requests</param>
    /// <returns>Batch pipeline result</returns>
    [HttpPost("process-batch")]
    [ProducesResponseType(typeof(BatchPipelineResult), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 500)]
    public async Task<IActionResult> ProcessBatchAsync([FromBody] List<DataPipelineRequest> requests)
    {
        if (requests == null || requests.Count == 0)
        {
            return BadRequest("No processing requests provided");
        }

        // Enforce batch size limit for DoS mitigation - use centralized constraint
        var constraints = _fileUploadService.GetUploadConstraints();
        if (requests.Count > constraints.MaxFilesPerBatch)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Batch size exceeded",
                Detail = $"Maximum {constraints.MaxFilesPerBatch} requests allowed per batch. Requested: {requests.Count}",
                Status = 400
            });
        }

        try
        {
            var result = await _orchestrator.ProcessBatchAsync(requests, HttpContext.RequestAborted);
            return Ok(result);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Batch file processing was cancelled");
            return StatusCode(499, new ProblemDetails
            {
                Title = "Request cancelled",
                Detail = "The batch file processing was cancelled by the client",
                Status = 499
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing batch files");
            return StatusCode(500, new ProblemDetails
            {
                Title = "Internal server error",
                Detail = "An error occurred while processing files",
                Status = 500
            });
        }
    }

    /// <summary>
    /// Get pipeline execution status
    /// </summary>
    /// <param name="executionId">The execution ID</param>
    /// <returns>Pipeline status</returns>
    [HttpGet("status/{executionId}")]
    [ProducesResponseType(typeof(PipelineStatusResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> GetPipelineStatusAsync(string executionId)
    {
        if (string.IsNullOrWhiteSpace(executionId))
        {
            return BadRequest("Execution ID is required");
        }

        try
        {
            var status = await _orchestrator.GetPipelineStatusAsync(executionId);
            return Ok(new PipelineStatusResponse
            {
                ExecutionId = executionId,
                Status = status
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pipeline status for {ExecutionId}", SanitizeForLogging(executionId));
            return StatusCode(500, new ProblemDetails
            {
                Title = "Internal server error",
                Detail = "An error occurred while retrieving pipeline status",
                Status = 500
            });
        }
    }

    /// <summary>
    /// Get pipeline metrics
    /// </summary>
    /// <param name="hours">Time window in hours (default: 24)</param>
    /// <returns>Pipeline metrics</returns>
    [HttpGet("metrics")]
    [ProducesResponseType(typeof(PipelineMetrics), 200)]
    public async Task<IActionResult> GetPipelineMetricsAsync([FromQuery] int hours = 24)
    {
        try
        {
            var timeWindow = TimeSpan.FromHours(Math.Max(1, Math.Min(168, hours))); // 1 hour to 1 week
            var metrics = await _orchestrator.GetPipelineMetricsAsync(timeWindow);
            return Ok(metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pipeline metrics");
            return StatusCode(500, new ProblemDetails
            {
                Title = "Internal server error",
                Detail = "An error occurred while retrieving pipeline metrics",
                Status = 500
            });
        }
    }

    /// <summary>
    /// Cancel a running pipeline execution
    /// </summary>
    /// <param name="executionId">The execution ID to cancel</param>
    /// <returns>Cancellation result</returns>
    [HttpPost("cancel/{executionId}")]
    [ProducesResponseType(typeof(CancelPipelineResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    public async Task<IActionResult> CancelPipelineAsync(string executionId)
    {
        if (string.IsNullOrWhiteSpace(executionId))
        {
            return BadRequest("Execution ID is required");
        }

        try
        {
            var cancelled = await _orchestrator.CancelPipelineAsync(executionId);
            return Ok(new CancelPipelineResponse
            {
                ExecutionId = executionId,
                Cancelled = cancelled
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling pipeline {ExecutionId}", SanitizeForLogging(executionId));
            return StatusCode(500, new ProblemDetails
            {
                Title = "Internal server error",
                Detail = "An error occurred while cancelling the pipeline",
                Status = 500
            });
        }
    }

    /// <summary>
    /// Execute immediate scheduled processing run
    /// </summary>
    /// <returns>Pipeline execution result</returns>
    [HttpPost("scheduled/execute")]
    [ProducesResponseType(typeof(PipelineExecutionResult), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 500)]
    public async Task<IActionResult> ExecuteScheduledProcessingAsync()
    {
        try
        {
            var result = await _scheduledService.ExecuteImmediateRunAsync(HttpContext.RequestAborted);
            return Ok(result);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Scheduled processing execution was cancelled");
            return StatusCode(499, new ProblemDetails
            {
                Title = "Request cancelled",
                Detail = "The scheduled processing execution was cancelled by the client",
                Status = 499
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing scheduled processing");
            return StatusCode(500, new ProblemDetails
            {
                Title = "Internal server error",
                Detail = "An error occurred while executing scheduled processing",
                Status = 500
            });
        }
    }

    /// <summary>
    /// Get scheduled processing statistics
    /// </summary>
    /// <returns>Scheduled processing stats</returns>
    [HttpGet("scheduled/stats")]
    [ProducesResponseType(typeof(ScheduledProcessingStats), 200)]
    public async Task<IActionResult> GetScheduledProcessingStatsAsync()
    {
        try
        {
            var stats = await _scheduledService.GetProcessingStatsAsync();
            return Ok(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting scheduled processing stats");
            return StatusCode(500, new ProblemDetails
            {
                Title = "Internal server error",
                Detail = "An error occurred while retrieving scheduled processing stats",
                Status = 500
            });
        }
    }

    /// <summary>
    /// Get pipeline health status
    /// </summary>
    /// <returns>Pipeline health status</returns>
    [HttpGet("health")]
    [ProducesResponseType(typeof(PipelineHealthStatus), 200)]
    public async Task<IActionResult> GetPipelineHealthAsync()
    {
        try
        {
            var health = await _monitoringService.GetHealthStatusAsync();
            return Ok(health);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pipeline health status");
            return StatusCode(500, new ProblemDetails
            {
                Title = "Internal server error",
                Detail = "An error occurred while retrieving pipeline health status",
                Status = 500
            });
        }
    }

    /// <summary>
    /// Get file upload constraints
    /// </summary>
    /// <returns>Upload constraints information</returns>
    [HttpGet("upload/constraints")]
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