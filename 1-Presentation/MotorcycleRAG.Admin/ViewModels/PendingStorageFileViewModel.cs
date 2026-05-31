using System.ComponentModel;
using System.Globalization;

namespace MotorcycleRAG.Admin.ViewModels;

/// <summary>
/// ViewModel for a blob-backed source file that is waiting to be processed.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Visibility", Justification = "Internal patterns")]
internal sealed class PendingStorageFileViewModel : INotifyPropertyChanged {
    private bool _isProcessing;

    public string UploadId { get; set; } = string.Empty;

    public string BlobName { get; set; } = string.Empty;

    public string DocumentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public DateTimeOffset LastModifiedUtc { get; set; }

    public string? LastKnownJobStatus { get; set; }

    public string? FailureReason { get; set; }

    public string? GraphImportStatus { get; set; }

    public string? GraphImportFailureReason { get; set; }

    public bool IsProcessing {
        get => _isProcessing;
        set {
            if (_isProcessing == value) {
                return;
            }

            _isProcessing = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsProcessing)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanStartPrimaryAction)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanStartGraphImport)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanDelete)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProcessButtonText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GraphImportButtonText)));
        }
    }

    public bool HasFailureReason => !string.IsNullOrWhiteSpace(FailureReason);

    public bool HasGraphWorkflow => string.Equals(DocumentType, "spec-dataset", StringComparison.OrdinalIgnoreCase);

    public bool HasGraphImportFailureReason => !string.IsNullOrWhiteSpace(GraphImportFailureReason);

    public string DisplayStatus => BuildWorkflowStatusText(
        HasGraphWorkflow ? "Search ingest" : "Ingestion",
        LastKnownJobStatus);

    public string GraphWorkflowStatusText => BuildWorkflowStatusText("Graph import", GraphImportStatus);

    public string DocumentTypeLabel => DocumentType switch {
        "manual-pdf" => "Manual PDF",
        "spec-dataset" => "Specification CSV",
        _ => DocumentType
    };

    public string SizeLabel => FormatFileSize(SizeBytes);

    public string ProcessButtonText => BuildPrimaryActionButtonText();

    public string GraphImportButtonText => BuildGraphActionButtonText();

    public bool CanStartPrimaryAction => !IsProcessing && CanStartWorkflow(LastKnownJobStatus);

    public bool CanStartGraphImport => HasGraphWorkflow && !IsProcessing && CanStartWorkflow(GraphImportStatus);

    public bool CanDelete => !IsProcessing;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Vis", Justification = "For binding")]
    public event PropertyChangedEventHandler? PropertyChanged;

    private string BuildPrimaryActionButtonText() {
        if (string.IsNullOrWhiteSpace(LastKnownJobStatus)) {
            return HasGraphWorkflow ? "Search Ingest" : "Process";
        }

        if (IsWorkflowRunning(LastKnownJobStatus)) {
            return HasGraphWorkflow ? "Search Running" : "Running";
        }

        if (IsWorkflowCompleted(LastKnownJobStatus)) {
            return HasGraphWorkflow ? "Search Done" : "Done";
        }

        return HasGraphWorkflow ? "Retry Search" : "Retry";
    }

    private string BuildGraphActionButtonText(string? workflowStatus = null) {
        var status = workflowStatus ?? GraphImportStatus;
        if (string.IsNullOrWhiteSpace(status)) {
            return "Graph Import";
        }

        if (IsWorkflowRunning(status)) {
            return "Graph Running";
        }

        if (IsWorkflowCompleted(status)) {
            return "Graph Done";
        }

        return "Retry Graph";
    }

    private static bool CanStartWorkflow(string? workflowStatus) {
        return string.IsNullOrWhiteSpace(workflowStatus)
               || string.Equals(workflowStatus, "Failed", StringComparison.OrdinalIgnoreCase)
               || string.Equals(workflowStatus, "Cancelled", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWorkflowRunning(string workflowStatus) {
        return string.Equals(workflowStatus, "Queued", StringComparison.OrdinalIgnoreCase)
               || string.Equals(workflowStatus, "Processing", StringComparison.OrdinalIgnoreCase)
               || string.Equals(workflowStatus, "Indexing", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWorkflowCompleted(string workflowStatus) {
        return string.Equals(workflowStatus, "Completed", StringComparison.OrdinalIgnoreCase)
               || string.Equals(workflowStatus, "PartiallyCompleted", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildWorkflowStatusText(string workflowName, string? workflowStatus) {
        return string.IsNullOrWhiteSpace(workflowStatus)
            ? $"{workflowName}: Not started"
            : $"{workflowName}: {workflowStatus}";
    }

    private static string FormatFileSize(long sizeBytes) {
        const double kilobyte = 1024d;
        const double megabyte = kilobyte * 1024d;
        const double gigabyte = megabyte * 1024d;

        return sizeBytes switch {
            >= (long)gigabyte => string.Format(CultureInfo.InvariantCulture, "{0:0.##} GB", sizeBytes / gigabyte),
            >= (long)megabyte => string.Format(CultureInfo.InvariantCulture, "{0:0.##} MB", sizeBytes / megabyte),
            >= (long)kilobyte => string.Format(CultureInfo.InvariantCulture, "{0:0.##} KB", sizeBytes / kilobyte),
            _ => string.Format(CultureInfo.InvariantCulture, "{0} B", sizeBytes)
        };
    }
}
