using SQLite;

namespace MotorcycleRAG.MobileApp.Persistence.Entities;

[Table("Citations")]
public class CitationEntity
{
    [PrimaryKey]
    public string Id { get; set; } = string.Empty;

    [Indexed]
    public string MessageId { get; set; } = string.Empty;

    public int Type { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Url { get; set; }

    public string? DocumentId { get; set; }

    public int? PageNumber { get; set; }

    public string? Section { get; set; }

    public int IsTappable { get; set; }
}
