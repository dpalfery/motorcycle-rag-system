using System;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Index schema configuration for different document types
/// </summary>
public class IndexSchemaConfiguration
{
    public string IndexName { get; set; } = string.Empty;
    public DocumentType DocumentType { get; set; }
    public FieldMappingConfiguration FieldMapping { get; set; } = new();
    public MetadataConfiguration Metadata { get; set; } = new();
    public bool EnableVectorSearch { get; set; } = true;
    public bool EnableSemanticSearch { get; set; } = true;
    public int VectorDimensions { get; set; } = 1536;
}

