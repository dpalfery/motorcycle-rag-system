namespace MotorcycleRAG.Domain.Enums;

/// <summary>
/// Type of ingestion job
/// </summary>
public enum IngestionJobType
{
    StructuredSpecification,
    PDFManual,
    WebContent,
    Batch,
    Scheduled
}
