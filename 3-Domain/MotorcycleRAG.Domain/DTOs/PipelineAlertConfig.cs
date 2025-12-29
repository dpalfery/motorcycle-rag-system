using System;
using System.Collections.Generic;

namespace MotorcycleRAG.Domain.DTOs;

/// <summary>
/// Pipeline alert configuration
/// </summary>
public class PipelineAlertConfig
{
    public bool IsEnabled { get; set; } = true;

    public AlertThresholds Thresholds { get; set; } = new();

    public List<string> EmailRecipients { get; set; } = new();

    public List<string> SlackChannels { get; set; } = new();

    public Dictionary<NotificationSeverity, bool> EnabledSeverities { get; set; } = new()
    {
        [NotificationSeverity.Info] = false,
        [NotificationSeverity.Warning] = true,
        [NotificationSeverity.Error] = true,
        [NotificationSeverity.Critical] = true
    };

    public TimeSpan AlertCooldown { get; set; } = TimeSpan.FromMinutes(15);

    public Dictionary<string, object> CustomSettings { get; set; } = new();
}

/// <summary>
/// Alert threshold configuration
/// </summary>
public class AlertThresholds
{
    public double FailureRateThreshold { get; set; } = 0.10; // 10% failure rate

    public TimeSpan LongRunningExecutionThreshold { get; set; } = TimeSpan.FromMinutes(30);

    public int ConsecutiveFailuresThreshold { get; set; } = 3;

    public TimeSpan HealthCheckFailureThreshold { get; set; } = TimeSpan.FromMinutes(5);

    public long MaxQueueSizeThreshold { get; set; } = 1000;

    public Dictionary<string, double> CustomThresholds { get; set; } = new();
}
