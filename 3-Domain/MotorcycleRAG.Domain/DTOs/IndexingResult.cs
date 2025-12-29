using System;
using System.Collections.Generic;

namespace MotorcycleRAG.Domain.DTOs;

/// <summary>
/// Result of indexing operations
/// </summary>
public class IndexingResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int DocumentsIndexed { get; set; }
    public List<string> Errors { get; set; } = new();
    public TimeSpan IndexingTime { get; set; }
    public string IndexName { get; set; } = string.Empty;
}
