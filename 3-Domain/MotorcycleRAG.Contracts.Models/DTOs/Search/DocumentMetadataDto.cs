using System.Collections.ObjectModel;

namespace MotorcycleRAG.Contracts.Models.DTOs.Search;

/// <summary>
/// Metadata carried with a search-index document.
/// </summary>
public class DocumentMetadataDto
{
    public string SourceFile { get; set; } = string.Empty;
    public Uri? SourceUrl { get; set; }
    public int PageNumber { get; set; }
    public string Section { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public DateTime PublishedDate { get; set; }
    public Collection<string> Tags { get; } = new();
    public Dictionary<string, object> AdditionalProperties { get; } = new();
}
