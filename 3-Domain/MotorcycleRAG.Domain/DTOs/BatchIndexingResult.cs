using System;
using System.Collections.Generic;

namespace MotorcycleRAG.Domain.DTOs;

/// <summary>
/// Batch indexing operation result
/// </summary>
public class BatchIndexingResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string IndexName { get; set; } = string.Empty;
    public int DocumentsProcessed { get; set; }
    public int DocumentsIndexed { get; set; }
    public List<string> Errors { get; set; } = new();
    public TimeSpan ProcessingTime { get; set; }
    public string BatchId { get; set; } = Guid.NewGuid().ToString();
}
