using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MotorcycleRAG.Contracts.Interfaces;
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
    private readonly IFileUploadService _fileUploadService;
    private readonly ILogger<DataPipelineUploadController> _logger;

    public DataPipelineUploadController(
        IFileUploadService fileUploadService,
        ILogger<DataPipelineUploadController> logger)
    {
        _fileUploadService = fileUploadService ?? throw new ArgumentNullException(nameof(fileUploadService));
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
    public async Task<IActionResult> UploadAsync(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "No file provided",
                Detail = "No file was provided or the file is empty",
                Status = StatusCodes.Status400BadRequest
            });
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
            var result = await _fileUploadService.UploadFileAsync(stream, metadata, options, HttpContext.RequestAborted);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file via legacy datapipeline route");
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Internal server error",
                Detail = "An error occurred while uploading the file",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }
}
