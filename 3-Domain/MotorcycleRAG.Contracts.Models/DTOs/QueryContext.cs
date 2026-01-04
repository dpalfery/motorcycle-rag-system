using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Query context for additional information
/// </summary>
public class QueryContext {
    public string SessionId { get; set; } = string.Empty;
    public Collection<string> PreviousQueries { get; } = new();
    public Dictionary<string, object> UserPreferences { get; } = new();
    public string Language { get; set; } = "en";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public bool RequiresMultiModal { get; set; }
    public string? CorrelationId { get; set; }
    public Dictionary<string, object> AdditionalProperties { get; } = new();
}
