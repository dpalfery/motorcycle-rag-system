using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MotorcycleRAG.Domain.InternalDTOs;

/// <summary>
/// Internal pipeline notification model (Domain-local copy)
/// </summary>
public class DomainPipelineNotification
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public DomainPipelineNotificationType Type { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string ExecutionId { get; set; } = string.Empty;

    public DomainPipelineType PipelineType { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public DomainNotificationSeverity Severity { get; set; }

    public Dictionary<string, object> Properties { get; } = new();

    public Collection<string> Recipients { get; } = new();
}

public enum DomainPipelineType
{
    CSV,
    PDF,
    Batch,
    Scheduled
}

/// <summary>
/// Internal pipeline notification types
/// </summary>
public enum DomainPipelineNotificationType
{
    ExecutionStarted,
    ExecutionCompleted,
    ExecutionFailed,
    ExecutionCancelled,
    HighFailureRate,
    LongRunningExecution,
    SystemAlert
}

/// <summary>
/// Internal notification severity levels
/// </summary>
public enum DomainNotificationSeverity
{
    Info,
    Warning,
    Error,
    Critical
}
