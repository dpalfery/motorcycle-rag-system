using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Interface for document intelligence client operations
/// </summary>
public interface IDocumentIntelligenceClient {
    /// <summary>
    /// Analyzes a document
    /// </summary>
    Task<DocumentAnalysisResult> AnalyzeDocumentAsync(string documentUrl);

    /// <summary>
    /// Analyzes document content from bytes
    /// </summary>
    Task<DocumentAnalysisResult> AnalyzeDocumentAsync(Stream documentStream, string contentType);
}

