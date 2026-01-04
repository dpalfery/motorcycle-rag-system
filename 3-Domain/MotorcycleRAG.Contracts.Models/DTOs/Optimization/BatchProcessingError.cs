namespace MotorcycleRAG.Domain.DTOs.Optimization;

/// <summary>
/// Error information for failed batch processing items.
/// </summary>
public class BatchProcessingError {
    public int ItemIndex { get; set; }
    public string ItemId { get; set; } = string.Empty;
    public Exception Exception { get; set; } = null!;
    public string ErrorMessage { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
