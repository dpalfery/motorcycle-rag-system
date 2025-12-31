using SQLite;

namespace MotorcycleRAG.MobileApp.Persistence.Entities;

[Table("Conversations")]
public class ConversationEntity
{
    [PrimaryKey]
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string CreatedAt { get; set; } = string.Empty; // ISO 8601

    public string UpdatedAt { get; set; } = string.Empty; // ISO 8601

    public int Status { get; set; }

    public long SizeBytes { get; set; }
}
