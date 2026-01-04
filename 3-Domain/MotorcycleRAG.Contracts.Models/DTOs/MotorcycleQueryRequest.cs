using System;
using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Domain.DTOs;

/// <summary>
/// Request model for motorcycle queries
/// </summary>
public class MotorcycleQueryRequest
{
    [Required]
    [StringLength(1000)]
    public string Query { get; set; } = string.Empty;

    public SearchPreferences Preferences { get; set; } = new();

    [StringLength(100)]
    public string UserId { get; set; } = string.Empty;

    public QueryContext Context { get; set; } = new();
}
