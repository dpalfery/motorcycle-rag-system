using System;

namespace MotorcycleRAG.MobileApp.Models;

public class UserMemory
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Category { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? SourceConversationId { get; set; }
    public DateTime ExtractedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;
}
