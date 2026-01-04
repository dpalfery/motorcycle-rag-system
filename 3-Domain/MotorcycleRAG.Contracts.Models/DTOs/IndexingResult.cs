using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Result of indexing operations
/// </summary>
public class IndexingResult {
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int DocumentsIndexed { get; set; }
    public Collection<string> Errors { get; } = new();
    public TimeSpan IndexingTime { get; set; }
    public string IndexName { get; set; } = string.Empty;
}

