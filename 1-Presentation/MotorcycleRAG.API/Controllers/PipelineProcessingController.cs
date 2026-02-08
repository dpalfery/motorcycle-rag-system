using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using System.Collections.ObjectModel;

namespace MotorcycleRAG.API.Controllers;

/// <summary>
/// Response for cancel pipeline operation
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "DTOs must be public for API documentation")]
public class CancelPipelineResponse
{
    public string ExecutionId { get; set; } = string.Empty;
    public bool Cancelled { get; set; }
}

/// <summary>
/// Response for get pipeline status operation
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "DTOs must be public for API documentation")]
public class PipelineStatusResponse
{
    public string ExecutionId { get; set; } = string.Empty;
    public PipelineStatus Status { get; set; }
}

/// <summary>
/// Controller for pipeline processing operations including file processing, status, and cancellation
/// </summary>
[ApiController]
[Route("api/pipeline-processing")]
[Produces("application/json")]
[Authorize(Policy = "mcr-api-admin")] // Require Admin policy for all processing operations
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "Controllers must be public for discovery")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1056:Uri properties should not be strings", Justification = "DTOs for API")]
public class PipelineProcessingController : ControllerBase
{
    private readonly IDataPipelineOrchestrator _orchestrator;
    private readonly ILogger<PipelineProcessingController> _logger;

    public PipelineProcessingController(
        IDataPipelineOrchestrator orchestrator,
        ILogger<PipelineProcessingController> logger)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
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
    /// Validates that the file path is within the allowed uploads directory to prevent path traversal.
    /// </summary>
    private bool IsSafeFilePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        // Define the root directory for uploaded files (should match your upload location)
        var uploadsRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "uploads"));

        var fullPath = Path.GetFullPath(filePath);

        // Ensure the file is within the uploads directory
        return fullPath.StartsWith(uploadsRoot, StringComparison.OrdinalIgnoreCase);
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

        // Validate file path to prevent path traversal
        if (string.IsNullOrWhiteSpace(request.FilePath) || !IsSafeFilePath(request.FilePath))
        {
            return BadRequest("File does not exist at the specified path or path is not allowed");
        }

#pragma warning disable CA3003 // Potential file path injection vulnerability - Validated by IsSafeFilePath
        if (!System.IO.File.Exists(request.FilePath))
        {
            return BadRequest("File does not exist at the specified path or path is not allowed");
        }
#pragma warning restore CA3003 // Potential file path injection vulnerability

        try
        {
            var result = await _orchestrator.ProcessFileAsync(request, HttpContext.RequestAborted);
            return Ok(result);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "File processing was cancelled");
            return StatusCode(499, new ProblemDetails
            {
                Title = "Request cancelled",
                Detail = "The file processing was cancelled by the client",
                Status = 499
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing file {FileName}", Path.GetFileName(request.FilePath));
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
    public async Task<IActionResult> ProcessBatchAsync([FromBody] Collection<DataPipelineRequest> requests)
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
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "Batch file processing was cancelled");
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
    /// Get pipeline metrics (default 24 hours)
    /// </summary>
    /// <returns>Pipeline metrics</returns>
    [HttpGet("metrics")]
    [ProducesResponseType(typeof(PipelineMetrics), 200)]
    public async Task<IActionResult> GetPipelineMetricsAsync()
    {
        return await GetPipelineMetricsInternalAsync(24);
    }

    /// <summary>
    /// Get pipeline metrics with custom time window
    /// </summary>
    /// <param name="hours">Time window in hours</param>
    /// <returns>Pipeline metrics</returns>
    [HttpGet("metrics/{hours:int}")]
    [ProducesResponseType(typeof(PipelineMetrics), 200)]
    public async Task<IActionResult> GetPipelineMetricsAsync(int hours)
    {
        return await GetPipelineMetricsInternalAsync(hours);
    }

    /// <summary>
    /// Internal implementation for getting pipeline metrics
    /// </summary>
    private async Task<IActionResult> GetPipelineMetricsInternalAsync(int hours)
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
}
