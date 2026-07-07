using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.API.Controllers;

/// <summary>
/// Backwards-compatible controller for legacy data pipeline upload route.
/// </summary>
[ApiController]
[Route("api/DataPipeline")]
[Produces("application/json")]
[Authorize(Policy = "mcr-api-admin")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "Controllers must be public for discovery")]
public sealed class DataPipelineUploadController : ControllerBase
{
    private readonly ILogger<DataPipelineUploadController> _logger;

    public DataPipelineUploadController(
        ILogger<DataPipelineUploadController> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Upload a single file for processing.
    /// Route: POST /api/DataPipeline/upload
    /// </summary>
    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(FileUploadResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status410Gone)]
    public IActionResult UploadAsync(IFormFile file)
    {
        _ = file;
        _logger.LogInformation("Legacy DataPipeline disk upload endpoint rejected with 410 Gone.");

        return StatusCode(StatusCodes.Status410Gone, new ProblemDetails
        {
            Title = "Legacy disk upload is no longer supported",
            Detail = "Use the blob-backed ingestion upload endpoint at /api/ingestion/jobs/upload.",
            Status = StatusCodes.Status410Gone
        });
    }
}
