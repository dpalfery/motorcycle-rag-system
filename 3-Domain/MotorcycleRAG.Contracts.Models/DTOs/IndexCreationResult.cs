using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MotorcycleRAG.Domain.DTOs;

/// <summary>
/// Result of index creation operations
/// </summary>
public class IndexCreationResult {
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public Collection<string> CreatedIndexes { get; } = new();
    public Collection<string> Errors { get; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
