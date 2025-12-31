using System;
using System.Collections.Generic;

namespace MotorcycleRAG.MobileApp.Models;

public enum MessageSender
{
    User,
    System
}

public enum MessageStatus
{
    Sending,
    Sent,
    Failed
}

public class ChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ConversationId { get; set; } = string.Empty;
    public MessageSender Sender { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public List<SourceCitation> Citations { get; set; } = new();
    public string? QueryId { get; set; }
    public MessageStatus Status { get; set; } = MessageStatus.Sent;
}
