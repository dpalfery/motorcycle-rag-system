namespace MotorcycleRAG.Domain.Enums;

/// <summary>
/// Type of ingestion job
/// </summary>
public enum IngestionJobType
{
    StructuredSpecification,
    BikeGraph,
    PDFManual,
    WebContent,
    Batch,
    Scheduled
}
