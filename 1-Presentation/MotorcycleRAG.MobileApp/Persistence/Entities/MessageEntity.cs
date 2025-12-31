using SQLite;

namespace MotorcycleRAG.MobileApp.Persistence.Entities;

[Table("Messages")]
public class MessageEntity
{
    [PrimaryKey]
    public string Id { get; set; } = string.Empty;

    [Indexed]
    public string ConversationId { get; set; } = string.Empty;

    public int Sender { get; set; }

    public string Content { get; set; } = string.Empty;

    public string Timestamp { get; set; } = string.Empty; // ISO 8601

    public string? QueryId { get; set; }

    public int Status { get; set; }
}
