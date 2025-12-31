using MotorcycleRAG.MobileApp.Models;
using MotorcycleRAG.MobileApp.Persistence.Entities;
using System;

namespace MotorcycleRAG.MobileApp.Persistence.Mappers;

public static class ConversationMapper
{
    public static ConversationEntity ToEntity(ConversationSession model)
    {
        return new ConversationEntity
        {
            Id = model.Id,
            Title = model.Title,
            CreatedAt = model.CreatedAt.ToString("O"),
            UpdatedAt = model.UpdatedAt.ToString("O"),
            Status = (int)model.Status,
            SizeBytes = model.SizeBytes
        };
    }

    public static ConversationSession ToModel(ConversationEntity entity)
    {
        return new ConversationSession
        {
            Id = entity.Id,
            Title = entity.Title,
            CreatedAt = DateTime.Parse(entity.CreatedAt),
            UpdatedAt = DateTime.Parse(entity.UpdatedAt),
            Status = (ConversationStatus)entity.Status,
            SizeBytes = entity.SizeBytes,
            Messages = new List<ChatMessage>() // Messages loaded separately
        };
    }
}
