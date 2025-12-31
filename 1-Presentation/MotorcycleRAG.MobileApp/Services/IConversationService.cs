using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MotorcycleRAG.MobileApp.Models;

namespace MotorcycleRAG.MobileApp.Services
{
    public interface IConversationService
    {
        Task<ConversationSession> CreateConversationAsync();
        Task<ConversationSession?> GetConversationAsync(Guid conversationId);
        Task<List<ConversationSession>> GetConversationsAsync();
        Task DeleteConversationAsync(Guid conversationId);
        Task<ChatMessage> SendMessageAsync(Guid conversationId, string messageText);
        Task SaveMessageAsync(Guid conversationId, ChatMessage message);
    }
}
