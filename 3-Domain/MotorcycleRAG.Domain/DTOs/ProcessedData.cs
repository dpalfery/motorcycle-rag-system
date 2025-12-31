using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using MotorcycleRAG.Domain.Entities;

namespace MotorcycleRAG.Domain.DTOs;

/// <summary>
/// Represents the result of processing a data file, including documents and metadata.
/// </summary>
public class ProcessedData
{
    public string Id { get; set; } = string.Empty;
    public List<MotorcycleDocument> Documents { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
}
