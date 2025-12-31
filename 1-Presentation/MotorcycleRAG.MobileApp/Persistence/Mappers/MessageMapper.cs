using MotorcycleRAG.MobileApp.Models;
using MotorcycleRAG.MobileApp.Persistence.Entities;
using System;

namespace MotorcycleRAG.MobileApp.Persistence.Mappers;

public static class MessageMapper
{
    public static MessageEntity ToEntity(ChatMessage model)
    {
        return new MessageEntity
        {
            Id = model.Id,
            ConversationId = model.ConversationId,
            Sender = (int)model.Sender,
            Content = model.Content,
            Timestamp = model.Timestamp.ToString("O"),
            QueryId = model.QueryId,
            Status = (int)model.Status
        };
    }

    public static ChatMessage ToModel(MessageEntity entity)
    {
        return new ChatMessage
        {
            Id = entity.Id,
            ConversationId = entity.ConversationId,
            Sender = (MessageSender)entity.Sender,
            Content = entity.Content,
            Timestamp = DateTime.Parse(entity.Timestamp),
            QueryId = entity.QueryId,
            Status = (MessageStatus)entity.Status,
            Citations = new List<SourceCitation>() // Citations loaded separately
        };
    }
}
