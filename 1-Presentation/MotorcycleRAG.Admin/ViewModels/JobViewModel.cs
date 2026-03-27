using System.Collections.ObjectModel;
using System.ComponentModel;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Admin.ViewModels;

/// <summary>
/// ViewModel for a legacy pipeline execution row.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Visibility", Justification = "Internal patterns")]
internal sealed class JobViewModel : INotifyPropertyChanged {
    private PipelineStatus _status;

    public string ExecutionId { get; set; } = string.Empty;

    public string PipelineType { get; set; } = string.Empty;

    public PipelineStatus Status {
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

    public DateTime StartTime { get; set; }

    public DateTime? EndTime { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public ObservableCollection<string> Errors { get; init; } = new();

    public ObservableCollection<string> Warnings { get; init; } = new();

    public bool IsRunning => Status == PipelineStatus.Processing
                             || Status == PipelineStatus.Queued
                             || Status == PipelineStatus.Indexing;

    public bool HasErrors => Errors.Count > 0;

    public bool HasWarnings => Warnings.Count > 0;

    public bool HasCreatedBy => !string.IsNullOrWhiteSpace(CreatedBy);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Vis", Justification = "For binding")]
    public event PropertyChangedEventHandler? PropertyChanged;
}
