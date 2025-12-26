namespace MotorcycleRAG.MobileApp.Models;

public enum SourceType
{
    PdfManual,
    WebSource,
    Dataset
}

public class SourceCitation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public SourceType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string? DocumentId { get; set; }
    public int? PageNumber { get; set; }
    public string? Section { get; set; }
    public bool IsTappable { get; set; }
}
