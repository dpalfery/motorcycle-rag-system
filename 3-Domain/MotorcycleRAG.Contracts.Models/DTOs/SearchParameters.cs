using System;
using System.Collections.Generic;

namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Search parameters for configuring search behavior at runtime
/// (Different from SearchOptions in Contracts/Options which is for configuration binding)
/// </summary>
public class SearchParameters {
    public int MaxResults { get; set; } = 10;
    public float MinRelevanceScore { get; set; } = 0.5f;
    public bool IncludeMetadata { get; set; } = true;
    public Dictionary<string, object> Filters { get; set; } = new();
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    public bool EnableCaching { get; set; } = true;
}
