using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Search preferences for customizing search behavior
/// </summary>
public class SearchPreferences {
    public bool IncludeWebSources { get; set; } = true;
    public bool IncludePDFSources { get; set; } = true;
    public int MaxResults { get; set; } = 10;
    public float MinRelevanceScore { get; set; } = 0.5f;
    public Collection<string> PreferredSources { get; } = new();
}
