using MotorcycleRAG.Core.Models;

namespace MotorcycleRAG.Core.Interfaces;

/// <summary>
/// Interface for Azure Document Intelligence client for PDF processing
/// </summary>
public interface IDocumentIntelligenceClient
{
    /// <summary>
    /// Analyze document using Layout model
    /// </summary>
    Task<DocumentAnalysisResult> AnalyzeDocumentAsync(
        byte[] document,
        string? contentType = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyze document from URI using Layout model
    /// </summary>
    Task<DocumentAnalysisResult> AnalyzeDocumentFromUriAsync(
        Uri documentUri,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if the Document Intelligence service is healthy
    /// </summary>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}