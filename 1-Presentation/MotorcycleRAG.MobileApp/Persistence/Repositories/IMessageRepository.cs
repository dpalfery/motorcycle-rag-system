using System.Collections.Generic;
using System.Threading.Tasks;
using MotorcycleRAG.MobileApp.Persistence.Entities;

namespace MotorcycleRAG.MobileApp.Persistence.Repositories;

public interface IMessageRepository
{
    Task<List<MessageEntity>> GetByConversationIdAsync(string conversationId);
    Task<int> InsertAsync(MessageEntity message);
    Task<int> UpdateAsync(MessageEntity message);
    Task<int> DeleteByConversationIdAsync(string conversationId);
}
