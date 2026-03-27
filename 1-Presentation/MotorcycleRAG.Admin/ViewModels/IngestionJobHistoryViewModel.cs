using System.ComponentModel;

namespace MotorcycleRAG.Admin.ViewModels;

/// <summary>
/// ViewModel for a storage-backed ingestion job row.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Visibility", Justification = "Internal patterns")]
internal sealed class IngestionJobHistoryViewModel : INotifyPropertyChanged {
    private string _status = string.Empty;
    private DateTimeOffset? _startedAtUtc;
    private DateTimeOffset? _completedAtUtc;
    private string? _failureReason;
    private string? _fabricRunId;

    public Guid JobId { get; set; }

    public string Status {
        get => _status;
        set {
            if (_status == value) {
                return;
            }

            _status = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRunning)));
        }
    }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? StartedAtUtc {
        get => _startedAtUtc;
        set {
            if (_startedAtUtc == value) {
                return;
            }

            _startedAtUtc = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StartedAtUtc)));
        }
    }

    public DateTimeOffset? CompletedAtUtc {
        get => _completedAtUtc;
        set {
            if (_completedAtUtc == value) {
                return;
            }

            _completedAtUtc = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CompletedAtUtc)));
        }
    }

    public string InputType { get; set; } = string.Empty;

    public string InputRef { get; set; } = string.Empty;

    public string? FailureReason {
        get => _failureReason;
        set {
            if (_failureReason == value) {
                return;
            }

            _failureReason = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FailureReason)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasFailureReason)));
        }
    }

    public string? FabricRunId {
        get => _fabricRunId;
        set {
            if (_fabricRunId == value) {
                return;
            }

            _fabricRunId = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FabricRunId)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasFabricRunId)));
        }
    }

    public bool IsRunning => string.Equals(Status, "Queued", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(Status, "Processing", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(Status, "Indexing", StringComparison.OrdinalIgnoreCase);

    public bool HasFailureReason => !string.IsNullOrWhiteSpace(FailureReason);

    public bool HasFabricRunId => !string.IsNullOrWhiteSpace(FabricRunId);

    public string InputTypeLabel => InputType switch {
        "PDFManual" => "Manual PDF",
        "StructuredSpecification" => "Specification CSV",
        "BikeGraph" => "Bike Graph Import",
        _ => InputType
    };

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Vis", Justification = "For binding")]
    public event PropertyChangedEventHandler? PropertyChanged;
}
