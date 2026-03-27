using System.ComponentModel;

namespace MotorcycleRAG.Admin.ViewModels;

internal sealed class LocalProcessorJobViewModel : INotifyPropertyChanged
{
    private bool _isImporting;

    public string JobId { get; set; } = string.Empty;

    public string UploadId { get; set; } = string.Empty;

    public string DocumentType { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public double Progress { get; set; }

    public DateTimeOffset? CreatedAtUtc { get; set; }

    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public int? NodesCreated { get; set; }

    public int? EdgesCreated { get; set; }

    public string? GraphImportStatus { get; set; }

    public string? GraphImportFailureReason { get; set; }

    public bool IsImporting
    {
        get => _isImporting;
        set
        {
            if (_isImporting == value)
            {
                return;
            }

            _isImporting = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsImporting)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanImportProcessedGraph)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImportButtonText)));
        }
    }

    public bool IsRunning =>
        string.Equals(Status, "processing", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Status, "queued", StringComparison.OrdinalIgnoreCase);

    public bool HasGraphImportFailureReason => !string.IsNullOrWhiteSpace(GraphImportFailureReason);

    public bool HasGraphImportStatus => !string.IsNullOrWhiteSpace(GraphImportStatus);

    public bool HasGraphMetrics => NodesCreated.HasValue || EdgesCreated.HasValue;

    public bool CanImportProcessedGraph =>
        !IsImporting &&
        string.Equals(DocumentType, "bike-graph", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Status, "completed", StringComparison.OrdinalIgnoreCase) &&
        (string.IsNullOrWhiteSpace(GraphImportStatus) ||
         string.Equals(GraphImportStatus, "Failed", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(GraphImportStatus, "Cancelled", StringComparison.OrdinalIgnoreCase));

    public string ImportButtonText
    {
        get
        {
            if (IsImporting)
            {
                return "Importing...";
            }

            if (string.IsNullOrWhiteSpace(GraphImportStatus))
            {
                return "Import To Graph";
            }

            if (string.Equals(GraphImportStatus, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                return "Imported";
            }

            if (string.Equals(GraphImportStatus, "Processing", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(GraphImportStatus, "Queued", StringComparison.OrdinalIgnoreCase))
            {
                return "Import Running";
            }

            return "Retry Import";
        }
    }

    public string DocumentTypeLabel => DocumentType switch
    {
        "bike-graph" => "Local Bike Graph",
        "spec-dataset" => "Local CSV",
        "manual-pdf" => "Local PDF",
        _ => DocumentType
    };

    public string ProgressLabel => $"{Math.Round(Progress * 100d):0}%";

    public string GraphMetricsLabel => $"Nodes: {NodesCreated ?? 0} | Edges: {EdgesCreated ?? 0}";

    public event PropertyChangedEventHandler? PropertyChanged;
}
