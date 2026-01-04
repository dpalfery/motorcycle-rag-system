using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MotorcycleRAG.Domain.ValueObjects;

/// <summary>
/// Document metadata for additional context
/// </summary>
public class DocumentMetadata {
    public string SourceFile { get; set; } = string.Empty;
    public Uri? SourceUrl { get; set; }
    public int PageNumber { get; set; }
    public string Section { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public DateTime PublishedDate { get; set; }
    public Collection<string> Tags { get; } = new();
    public Dictionary<string, object> AdditionalProperties { get; } = new();
}
