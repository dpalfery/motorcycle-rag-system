using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Field mapping configuration for index creation
/// </summary>
public class FieldMappingConfiguration {
    public Dictionary<string, string> FieldMappings { get; } = new();
    public Collection<string> SearchableFields { get; } = new();
    public Collection<string> FilterableFields { get; } = new();
    public Collection<string> FacetableFields { get; } = new();
    public Collection<string> SortableFields { get; } = new();
}

