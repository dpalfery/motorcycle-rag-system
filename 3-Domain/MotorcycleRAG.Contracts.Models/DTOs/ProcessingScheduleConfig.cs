using System;
using System.Collections.Generic;

namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Configuration for processing schedules
/// </summary>
public class ProcessingScheduleConfig {
    public bool IsEnabled { get; set; } = true;

    public string CronExpression { get; set; } = "0 2 * * *"; // Daily at 2 AM (minute, hour, day, month, day-of-week)

    public TimeSpan ProcessingWindow { get; set; } = TimeSpan.FromHours(4);

    public int MaxConcurrentJobs { get; set; } = 3;

    public string ProcessingDirectory { get; set; } = "scheduled";

    public bool ProcessPendingUploads { get; set; } = true;

    public Dictionary<string, object> CustomSettings { get; set; } = new();
}
