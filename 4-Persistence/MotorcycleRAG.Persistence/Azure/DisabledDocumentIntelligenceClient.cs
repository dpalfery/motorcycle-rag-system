using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Persistence.Azure;

/// <summary>
/// Predictable fallback used when Azure Document Intelligence is not configured.
/// </summary>
public sealed class DisabledDocumentIntelligenceClient : IDocumentIntelligenceClient
{
    private const string ErrorMessage =
        "Azure Document Intelligence is not configured for this environment. " +
        "Use POST /api/ingestion/jobs/upload and POST /api/ingestion/jobs to run PDF ingestion through the local Python processor.";

    private readonly ILogger<DisabledDocumentIntelligenceClient> _logger;

    public DisabledDocumentIntelligenceClient(ILogger<DisabledDocumentIntelligenceClient> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<DocumentAnalysisResult> AnalyzeDocumentAsync(Uri documentUri)
    {
        ArgumentNullException.ThrowIfNull(documentUri);
        _logger.LogWarning("Document Intelligence URI analysis was requested while disabled.");
        throw new InvalidOperationException(ErrorMessage);
    }

    public Task<DocumentAnalysisResult> AnalyzeDocumentAsync(Stream documentStream, string contentType)
    {
        ArgumentNullException.ThrowIfNull(documentStream);
        _logger.LogWarning("Document Intelligence stream analysis was requested while disabled. ContentType: {ContentType}", contentType);
        throw new InvalidOperationException(ErrorMessage);
    }
}
