using System;
using System.Collections.Generic;

namespace MotorcycleRAG.Domain.DTOs;

/// <summary>
/// Pipeline execution context
/// </summary>
public class PipelineExecutionContext {
    public string UserId { get; set; } = string.Empty;

    public string SessionId { get; set; } = string.Empty;

    public string CorrelationId { get; set; } = string.Empty;

    public Dictionary<string, string> Properties { get; set; } = new();

    public DateTime RequestTime { get; set; } = DateTime.UtcNow;

    public string Source { get; set; } = "API";
}
