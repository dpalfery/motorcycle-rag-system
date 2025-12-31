using SQLite;

namespace MotorcycleRAG.MobileApp.Persistence.Entities;

[Table("UserMemory")]
public class UserMemoryEntity
{
    [PrimaryKey]
    public string Id { get; set; } = string.Empty;

    [Indexed]
    public string Category { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public string? SourceConversationId { get; set; }

    public string ExtractedAt { get; set; } = string.Empty; // ISO 8601

    public string? UpdatedAt { get; set; } // ISO 8601

    [Indexed]
    public int IsActive { get; set; }
}
