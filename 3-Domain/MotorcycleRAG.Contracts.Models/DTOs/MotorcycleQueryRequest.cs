using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MotorcycleRAG.Contracts.Models.Serialization;
using MotorcycleRAG.Domain.ValueObjects;

namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Request model for motorcycle queries
/// </summary>
public class MotorcycleQueryRequest {
    [Required]
    [StringLength(1000)]
    public string Query { get; set; } = string.Empty;

    public SearchPreferences Preferences { get; set; } = new();

    [StringLength(100)]
    public string UserId { get; set; } = string.Empty;

    public QueryContext Context { get; set; } = new();

    /// <summary>
    /// Optional motorcycle category filter (Dirt, Touring, Sport, Cruiser). When
    /// supplied, the query is restricted to the matching category-partitioned index.
    /// When omitted, the query fans out across all four indexes. Nullable so existing
    /// callers that omit it continue to compile.
    /// </summary>
    [JsonConverter(typeof(MotorcycleCategoryJsonConverter))]
    public MotorcycleCategory? Category { get; set; }
}
