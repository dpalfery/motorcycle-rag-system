using System;
using System.Collections.Generic;

namespace MotorcycleRAG.Domain.DTOs;

/// <summary>
/// Query context for additional information
/// </summary>
public class QueryContext
{
    public string SessionId { get; set; } = string.Empty;
    public List<string> PreviousQueries { get; set; } = new();
    public Dictionary<string, object> UserPreferences { get; set; } = new();
    public string Language { get; set; } = "en";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public bool RequiresMultiModal { get; set; }
    public string? CorrelationId { get; set; }
    public Dictionary<string, object> AdditionalProperties { get; set; } = new();
}
