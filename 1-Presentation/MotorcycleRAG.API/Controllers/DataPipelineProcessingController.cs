using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.API.Controllers;

/// <summary>
/// Backwards-compatible controller for legacy data pipeline processing and status routes.
/// </summary>
/// <remarks>
/// This controller is a legacy backwards-compatible route (<c>api/DataPipeline/*</c>).
/// New ingestion flows should use <see cref="IngestionJobsController"/> (<c>api/ingestion/*</c>).
/// The <see cref="DataPipelineRequest"/> DTO accepted by <see cref="ProcessAsync"/> includes a
/// <c>FilePath</c> property — callers MUST supply an opaque blob reference (e.g. an upload GUID
/// or blob key), never a raw filesystem path, to avoid path traversal (CWE-22).
/// </remarks>
[ApiController]
[Route("api/DataPipeline")]
[Produces("application/json")]
[Authorize(Policy = "mcr-api-admin")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "Controllers must be public for discovery")]
public sealed class DataPipelineProcessingController : ControllerBase
{
    private readonly IDataPipelineOrchestrator _orchestrator;
    private readonly ILogger<DataPipelineProcessingController> _logger;

    public DataPipelineProcessingController(
        IDataPipelineOrchestrator orchestrator,
        ILogger<DataPipelineProcessingController> logger)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Process an uploaded file.
    /// Route: POST /api/DataPipeline/process
    /// </summary>
    // SECURITY: Callers must pass an opaque blob key in request.FilePath, not a raw filesystem path. See DataPipelineRequest.FilePath documentation.
    [HttpPost("process")]
    [ProducesResponseType(typeof(PipelineExecutionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ProcessAsync([FromBody] DataPipelineRequest request)
    {
        if (request == null)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Request is required",
                Status = StatusCodes.Status400BadRequest
            });
        }

        try
        {
            var result = await _orchestrator.ProcessFileAsync(request, HttpContext.RequestAborted);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing file via legacy datapipeline route");
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Internal server error",
                Detail = "An error occurred while processing the file",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Get pipeline execution status.
    /// Route: GET /api/DataPipeline/status/{executionId}
    /// </summary>
    // NOTE: Legacy status polling. Prefer GET /api/ingestion/jobs/{jobId} for Fabric pipeline jobs.
    [HttpGet("status/{executionId}")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetStatusAsync(string executionId)
    {
        if (string.IsNullOrWhiteSpace(executionId))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Execution ID is required",
                Status = StatusCodes.Status400BadRequest
            });
        }

        try
        {
            var status = await _orchestrator.GetPipelineStatusAsync(executionId);
            return Ok(new { executionId, status });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pipeline status via legacy datapipeline route");
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Internal server error",
                Detail = "An error occurred while retrieving pipeline status",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }
}
