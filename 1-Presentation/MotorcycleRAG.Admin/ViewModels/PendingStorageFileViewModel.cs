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

    public bool IsProcessing {
        get => _isProcessing;
        set {
            if (_isProcessing == value) {
                return;
            }

            _isProcessing = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsProcessing)));
        }
    }

    public bool HasFailureReason => !string.IsNullOrWhiteSpace(FailureReason);

    public string DisplayStatus => string.IsNullOrWhiteSpace(LastKnownJobStatus)
        ? "Not started"
        : $"Last attempt: {LastKnownJobStatus}";

    public string DocumentTypeLabel => DocumentType switch {
        "manual-pdf" => "Manual PDF",
        "spec-dataset" => "Specification CSV",
        _ => DocumentType
    };

    public string SizeLabel => FormatFileSize(SizeBytes);

    public string ProcessButtonText => IsRetry ? "Retry" : "Process";

    private bool IsRetry => !string.IsNullOrWhiteSpace(LastKnownJobStatus);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Vis", Justification = "For binding")]
    public event PropertyChangedEventHandler? PropertyChanged;

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
