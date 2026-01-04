using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MotorcycleRAG.Domain.DTOs;

/// <summary>
/// Batch pipeline processing result
/// </summary>
public class BatchPipelineResult {
    public string BatchId { get; set; } = Guid.NewGuid().ToString();

    public DateTime StartTime { get; set; } = DateTime.UtcNow;

    public DateTime? EndTime { get; set; }

    public int TotalFiles { get; set; }

    public int ProcessedSuccessfully { get; set; }

    public int ProcessedWithErrors { get; set; }

    public int Failed { get; set; }

    public Collection<PipelineExecutionResult> Results { get; } = new();

    public Dictionary<string, object> BatchMetrics { get; } = new();

    public bool IsCompleted => EndTime.HasValue;

    public bool HasErrors => Failed > 0 || ProcessedWithErrors > 0;
}
