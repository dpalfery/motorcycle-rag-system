using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Pipeline alert configuration
/// </summary>
public class PipelineAlertConfig {
    public bool IsEnabled { get; set; } = true;

    public AlertThresholds Thresholds { get; set; } = new();

    public Collection<string> EmailRecipients { get; } = new();

    public Collection<string> SlackChannels { get; } = new();

    public Dictionary<NotificationSeverity, bool> EnabledSeverities { get; } = new() {
        [NotificationSeverity.Info] = false,
        [NotificationSeverity.Warning] = true,
        [NotificationSeverity.Error] = true,
        [NotificationSeverity.Critical] = true
    };

    public TimeSpan AlertCooldown { get; set; } = TimeSpan.FromMinutes(15);

    public Dictionary<string, object> CustomSettings { get; } = new();
}

/// <summary>
/// Alert threshold configuration
/// </summary>
public class AlertThresholds {
    public double FailureRateThreshold { get; set; } = 0.10; // 10% failure rate

    public TimeSpan LongRunningExecutionThreshold { get; set; } = TimeSpan.FromMinutes(30);

    public int ConsecutiveFailuresThreshold { get; set; } = 3;

    public TimeSpan HealthCheckFailureThreshold { get; set; } = TimeSpan.FromMinutes(5);

    public long MaxQueueSizeThreshold { get; set; } = 1000;

    public Dictionary<string, double> CustomThresholds { get; } = new();
}
