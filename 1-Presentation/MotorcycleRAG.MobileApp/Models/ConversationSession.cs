using System;
using System.Collections.Generic;

namespace MotorcycleRAG.MobileApp.Models;

public enum ConversationStatus
{
    Active,
    Archived,
    Deleted
}

public class ConversationSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "New Conversation";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<ChatMessage> Messages { get; set; } = new();
    public ConversationStatus Status { get; set; } = ConversationStatus.Active;
    public long SizeBytes { get; set; }
}
