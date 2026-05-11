namespace MotorcycleRAG.Contracts.Models.DTOs.ManualIngestion;

public record RegisterManualDocumentRequest(
    string SourceFileName,
    string DocumentType,
    string? Make,
    string? Model,
    int? Year,
    string? ContentHash);
