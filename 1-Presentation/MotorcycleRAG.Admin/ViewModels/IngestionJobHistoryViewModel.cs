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
    private string? _docIngestionRunId;
    private bool _isActionInProgress;

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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFailedTerminal)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFinishedTerminal)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsTerminal)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanDelete)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRetry)));
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

    public string? DocIngestionRunId {
        get => _docIngestionRunId;
        set {
            if (_docIngestionRunId == value) {
                return;
            }

            _docIngestionRunId = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DocIngestionRunId)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasDocIngestionRunId)));
        }
    }

    public bool IsActionInProgress {
        get => _isActionInProgress;
        set {
            if (_isActionInProgress == value) {
                return;
            }

            _isActionInProgress = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActionInProgress)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanDelete)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRetry)));
        }
    }

    public bool IsRunning => string.Equals(Status, "Queued", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(Status, "Processing", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(Status, "Indexing", StringComparison.OrdinalIgnoreCase);

    public bool IsFailedTerminal => string.Equals(Status, "Failed", StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(Status, "Cancelled", StringComparison.OrdinalIgnoreCase);

    public bool IsFinishedTerminal => string.Equals(Status, "Completed", StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(Status, "PartiallyCompleted", StringComparison.OrdinalIgnoreCase);

    public bool IsTerminal => IsFailedTerminal || IsFinishedTerminal;

    public bool CanDelete => IsTerminal && !IsActionInProgress;

    public bool CanRetry => IsFailedTerminal && !IsActionInProgress;

    public bool HasFailureReason => !string.IsNullOrWhiteSpace(FailureReason);

    public bool HasDocIngestionRunId => !string.IsNullOrWhiteSpace(DocIngestionRunId);

    public string InputTypeLabel => InputType switch {
        "PDFManual" => "Manual PDF",
        "StructuredSpecification" => "Specification CSV",
        "BikeGraph" => "Bike Graph Import",
        _ => InputType
    };

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Vis", Justification = "For binding")]
    public event PropertyChangedEventHandler? PropertyChanged;
}
