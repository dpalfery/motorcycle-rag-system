using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using MotorcycleRAG.Domain.Entities;

namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Represents the result of processing a data file, including documents and metadata.
/// </summary>
public class ProcessedData {
    public string Id { get; set; } = string.Empty;
    public Collection<MotorcycleDocument> Documents { get; } = new();
    public Dictionary<string, object> Metadata { get; } = new();
}

