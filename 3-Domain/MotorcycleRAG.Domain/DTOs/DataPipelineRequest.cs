using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Domain.DTOs;

/// <summary>
/// Request model for data pipeline processing
/// </summary>
public class DataPipelineRequest
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    public string FileName { get; set; } = string.Empty;

    [Required]
    public string FilePath { get; set; } = string.Empty;

    [Required]
    public FileType FileType { get; set; }

    public Dictionary<string, object> Metadata { get; set; } = new();

    public PipelineOptions Options { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string CreatedBy { get; set; } = string.Empty;
}
