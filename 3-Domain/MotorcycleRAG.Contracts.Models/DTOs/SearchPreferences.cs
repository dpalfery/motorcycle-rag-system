using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using MotorcycleRAG.Contracts.Models.Serialization;
using MotorcycleRAG.Domain.ValueObjects;

namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Search preferences for customizing search behavior
/// </summary>
public class SearchPreferences {
    public bool IncludeWebSources { get; set; } = true;
    public bool IncludePDFSources { get; set; } = true;
    public int MaxResults { get; set; } = 10;
    public float MinRelevanceScore { get; set; } = 0.5f;
    public Collection<string> PreferredSources { get; set; } = new();

    /// <summary>
    /// Optional motorcycle category filter (Dirt, Touring, Sport, Cruiser). When
    /// supplied, restricts retrieval to the matching category-partitioned index;
    /// when omitted, retrieval fans out across all four indexes.
    /// </summary>
    [JsonConverter(typeof(MotorcycleCategoryJsonConverter))]
    public MotorcycleCategory? Category { get; set; }
}
