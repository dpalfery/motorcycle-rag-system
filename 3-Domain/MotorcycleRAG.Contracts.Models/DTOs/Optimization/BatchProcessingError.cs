namespace MotorcycleRAG.Contracts.Models.DTOs.Optimization;

/// <summary>
/// Error information for failed batch processing items.
/// </summary>
public class BatchProcessingError {
    public int ItemIndex { get; set; }
    public string ItemId { get; set; } = string.Empty;
    public string? ExceptionType { get; set; }
    public string? ExceptionMessage { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
