using System;
using System.Collections.Generic;

namespace MotorcycleRAG.Domain.DTOs;

/// <summary>
/// Result of index creation operations
/// </summary>
public class IndexCreationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> CreatedIndexes { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
