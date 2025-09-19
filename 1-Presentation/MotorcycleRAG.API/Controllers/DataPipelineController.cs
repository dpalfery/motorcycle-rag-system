using Microsoft.AspNetCore.Mvc;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Models;
using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.API.Controllers;

/// <summary>
/// Controller for data pipeline operations including file upload and processing
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
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

            var uploadResult = await _fileUploadService.UploadFileAsync(file, options, HttpContext.RequestAborted);

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file {FileName}", file?.FileName);
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

        try
        {
            var options = new FileUploadOptions
            {
                ValidateFileContent = true,
                GenerateUniqueFileName = true
            };

            var uploadResult = await _fileUploadService.UploadFilesAsync(files, options, HttpContext.RequestAborted);

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing file {FilePath}", request.FilePath);
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

        try
        {
            var result = await _orchestrator.ProcessBatchAsync(requests, HttpContext.RequestAborted);
            return Ok(result);
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
    [ProducesResponseType(typeof(PipelineStatus), 200)]
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
            return Ok(new { ExecutionId = executionId, Status = status });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pipeline status for {ExecutionId}", executionId);
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
    [ProducesResponseType(typeof(bool), 200)]
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
            return Ok(new { ExecutionId = executionId, Cancelled = cancelled });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling pipeline {ExecutionId}", executionId);
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