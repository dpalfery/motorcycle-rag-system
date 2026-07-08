using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using MotorcycleRAG.Contracts.Models.Serialization;
using MotorcycleRAG.Domain.ValueObjects;

namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Search context for agent coordination
/// </summary>
public class SearchContext {
    public string SessionId { get; set; } = string.Empty;
    public SearchPreferences Preferences { get; set; } = new();
    public QueryContext QueryContext { get; set; } = new();
    public Dictionary<string, object> AdditionalContext { get; set; } = new();

    /// <summary>
    /// Optional motorcycle category filter (Dirt, Touring, Sport, Cruiser) used to
    /// route agent search to a single category-partitioned index. When omitted, search
    /// fans out across all four indexes.
    /// </summary>
    [JsonConverter(typeof(MotorcycleCategoryJsonConverter))]
    public MotorcycleCategory? Category { get; set; }
}
