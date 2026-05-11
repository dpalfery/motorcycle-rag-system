namespace MotorcycleRAG.Contracts.Models.DTOs.ManualIngestion;

public enum ManualDocumentStatus
{
    Pending,
    Canonicalized,
    Processing,
    Processed,
    Failed
}
