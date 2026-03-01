using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MotorcycleRAG.API.Configuration;
using MotorcycleRAG.Application.Pipeline;

namespace MotorcycleRAG.API.Controllers;

/// <summary>
/// Serves manual page images to authorised users.
/// </summary>
[ApiController]
[Route("api/manuals")]
[Produces("image/png", "application/json")]
[Authorize(Policy = AuthorizationPolicyNames.ManualsView)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "Controllers must be public for discovery")]
[EnableRateLimiting("manuals-view")]
public sealed class ManualsController : ControllerBase
{
    private readonly IManualPageQueryService _queryService;
    private readonly ILogger<ManualsController> _logger;

    public ManualsController(
        IManualPageQueryService queryService,
        ILogger<ManualsController> logger)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Retrieves a single page image from a manual.
    /// </summary>
    /// <param name="manualId">The manual document GUID.</param>
    /// <param name="pageNumber">1-based page number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The page image as a PNG file.</returns>
    [HttpGet("{manualId:guid}/pages/{pageNumber:int}")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPage(Guid manualId, int pageNumber, CancellationToken ct)
    {
        if (pageNumber <= 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid page number",
                Detail = "Page number must be a positive integer.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        // SECURITY: Log only manualId — never combine with pageNumber (correlation risk).
        _logger.LogInformation("Manual page retrieval requested. ManualId={ManualId}", manualId);

        try
        {
            var result = await _queryService.GetPageAsync(manualId, pageNumber, ct).ConfigureAwait(false);

            Response.Headers.CacheControl = "private, max-age=600";
            Response.Headers.ContentDisposition = $"inline; filename=\"{manualId}-p{pageNumber}.png\"";

            if (result.ETag is not null)
            {
                Response.Headers.ETag = result.ETag;
            }

            return File(result.Content, "image/png");
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Page not found",
                Detail = "The requested manual page does not exist.",
                Status = StatusCodes.Status404NotFound
            });
        }
    }
}