using System;
using System.Collections.Generic;

namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Metadata management configuration
/// </summary>
public class MetadataConfiguration
{
    public bool IncludeSourceMetadata { get; set; } = true;
    public bool IncludeProcessingMetadata { get; set; } = true;
    public List<string> RequiredMetadataFields { get; set; } = new();
    public Dictionary<string, object> DefaultMetadataValues { get; set; } = new();
}

