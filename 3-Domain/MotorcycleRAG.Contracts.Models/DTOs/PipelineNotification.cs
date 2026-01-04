using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace MotorcycleRAG.Domain.DTOs;

/// <summary>
/// Pipeline notification model
/// </summary>
public class PipelineNotification {
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public PipelineNotificationType Type { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string ExecutionId { get; set; } = string.Empty;

    public PipelineType PipelineType { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public NotificationSeverity Severity { get; set; }

    public Dictionary<string, object> Properties { get; } = new();

    public Collection<string> Recipients { get; } = new();
}

/// <summary>
/// Pipeline notification types
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PipelineNotificationType {
    ExecutionStarted,
    ExecutionCompleted,
    ExecutionFailed,
    ExecutionCancelled,
    HighFailureRate,
    LongRunningExecution,
    SystemAlert
}

/// <summary>
/// Notification severity levels
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NotificationSeverity {
    Info,
    Warning,
    Error,
    Critical
}

