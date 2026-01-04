using System;
using System.Collections.Generic;

namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Pipeline processing options
/// </summary>
public class PipelineOptions {
    public bool ProcessImages { get; set; } = true;

    public bool GenerateEmbeddings { get; set; } = true;

    public bool IndexImmediately { get; set; } = true;

    public int BatchSize { get; set; } = 100;

    public int MaxRetries { get; set; } = 3;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(30);

    public Dictionary<string, object> CustomOptions { get; set; } = new();
}

