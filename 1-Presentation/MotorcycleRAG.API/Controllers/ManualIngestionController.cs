using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MotorcycleRAG.API.Configuration;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Contracts.Models.DTOs.ManualIngestion;

namespace MotorcycleRAG.API.Controllers;

[ApiController]
[Route("api/manual-ingestion")]
public class ManualIngestionController : ControllerBase
{
    private readonly IManualIngestionService _service;
    private readonly ILogger<ManualIngestionController> _logger;

    public ManualIngestionController(IManualIngestionService service, ILogger<ManualIngestionController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpPost("manual-documents")]
    [Authorize(Policy = AuthorizationPolicyNames.Admin)]
    public async Task<ActionResult<ManualDocumentDto>> RegisterDocument([FromForm] RegisterManualDocumentRequest request, IFormFile file, CancellationToken ct)
    {
        if (file == null || file.Length == 0) return BadRequest("File is required");
        
        using var stream = file.OpenReadStream();
        var result = await _service.RegisterManualAsync(request, stream, ct);
        return Ok(result);
    }

    [HttpGet("manual-documents/{documentId:guid}")]
    [Authorize(Policy = AuthorizationPolicyNames.Admin)]
    public async Task<ActionResult<ManualDocumentDto>> GetDocument(Guid documentId, CancellationToken ct)
    {
        var result = await _service.GetDocumentAsync(documentId, ct);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpGet("manual-documents")]
    [Authorize(Policy = AuthorizationPolicyNames.Admin)]
    public async Task<ActionResult<IEnumerable<ManualDocumentDto>>> GetAllDocuments(CancellationToken ct)
    {
        var result = await _service.GetAllDocumentsAsync(ct);
        return Ok(result);
    }

    [HttpPost("manual-documents/{documentId:guid}/runs")]
    [Authorize(Policy = AuthorizationPolicyNames.Admin)]
    public async Task<ActionResult<ManualRunDto>> CreateRun(Guid documentId, [FromBody] CreateManualRunRequest request, CancellationToken ct)
    {
        if (documentId != request.DocumentId) return BadRequest("DocumentId mismatch");
        var result = await _service.CreateRunAsync(request, ct);
        return Ok(result);
    }

    [HttpGet("manual-documents/{documentId:guid}/runs")]
    [Authorize(Policy = AuthorizationPolicyNames.Admin)]
    public async Task<ActionResult<IEnumerable<ManualRunDto>>> GetRunsForDocument(Guid documentId, CancellationToken ct)
    {
        var result = await _service.GetRunsForDocumentAsync(documentId, ct);
        return Ok(result);
    }

    [HttpGet("manual-runs/{runId:guid}")]
    [Authorize(Policy = AuthorizationPolicyNames.Admin)]
    public async Task<ActionResult<ManualRunDto>> GetRun(Guid runId, CancellationToken ct)
    {
        var result = await _service.GetRunAsync(runId, ct);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpGet("manual-runs/{runId:guid}/stages")]
    [Authorize(Policy = AuthorizationPolicyNames.Admin)]
    public async Task<ActionResult<IEnumerable<ManualStageDto>>> GetStagesForRun(Guid runId, CancellationToken ct)
    {
        var result = await _service.GetStagesForRunAsync(runId, ct);
        return Ok(result);
    }

    [HttpPost("manual-runs/{runId:guid}/stages/{stageName}/start")]
    [Authorize(Policy = AuthorizationPolicyNames.LocalProcessor)]
    public async Task<IActionResult> ReportStageStart(Guid runId, string stageName, [FromBody] ManualStageStartRequest request, CancellationToken ct)
    {
        await _service.ReportStageStartAsync(runId, stageName, request, ct);
        return Ok();
    }

    [HttpPost("manual-runs/{runId:guid}/stages/{stageName}/complete")]
    [Authorize(Policy = AuthorizationPolicyNames.LocalProcessor)]
    public async Task<IActionResult> ReportStageComplete(Guid runId, string stageName, [FromBody] ManualStageCompleteRequest request, CancellationToken ct)
    {
        await _service.ReportStageCompleteAsync(runId, stageName, request, ct);
        return Ok();
    }

    [HttpPost("manual-runs/{runId:guid}/stages/{stageName}/fail")]
    [Authorize(Policy = AuthorizationPolicyNames.LocalProcessor)]
    public async Task<IActionResult> ReportStageFail(Guid runId, string stageName, [FromBody] ManualStageFailRequest request, CancellationToken ct)
    {
        await _service.ReportStageFailAsync(runId, stageName, request, ct);
        return Ok();
    }

    [HttpPost("manual-runs/{runId:guid}/artifacts")]
    [Authorize(Policy = AuthorizationPolicyNames.LocalProcessor)]
    public async Task<IActionResult> RegisterArtifact(Guid runId, [FromBody] ManualArtifactRegistrationRequest request, CancellationToken ct)
    {
        await _service.RegisterArtifactAsync(runId, request, ct);
        return Ok();
    }

    [HttpPost("graph-seeding-jobs")]
    [Authorize(Policy = AuthorizationPolicyNames.Admin)]
    public async Task<ActionResult<IngestionJobStatusResponse>> CreateGraphSeedJob([FromBody] CreateGraphSeedJobRequest request, CancellationToken ct)
    {
        var userId = User.FindFirst("sub")?.Value ?? "unknown";
        var result = await _service.CreateGraphSeedJobAsync(request, userId, ct);
        return Accepted(result);
    }

    [HttpGet("operations")]
    [Authorize(Policy = AuthorizationPolicyNames.Admin)]
    public async Task<ActionResult<IEnumerable<UnifiedOperationDto>>> GetOperations(CancellationToken ct)
    {
        var result = await _service.GetOperationsAsync(ct);
        return Ok(result);
    }
}
