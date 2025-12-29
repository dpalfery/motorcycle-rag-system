using System;
using System.Collections.Generic;

namespace MotorcycleRAG.Domain.DTOs;

/// <summary>
/// Field mapping configuration for index creation
/// </summary>
public class FieldMappingConfiguration
{
    public Dictionary<string, string> FieldMappings { get; set; } = new();
    public List<string> SearchableFields { get; set; } = new();
    public List<string> FilterableFields { get; set; } = new();
    public List<string> FacetableFields { get; set; } = new();
    public List<string> SortableFields { get; set; } = new();
}
