using System;
using System.Collections.Generic;

namespace MotorcycleRAG.Domain.DTOs;

/// <summary>
/// Search context for agent coordination
/// </summary>
public class SearchContext
{
    public string SessionId { get; set; } = string.Empty;
    public SearchPreferences Preferences { get; set; } = new();
    public QueryContext QueryContext { get; set; } = new();
    public Dictionary<string, object> AdditionalContext { get; set; } = new();
}
