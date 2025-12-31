using System.Collections.Generic;
using System.Threading.Tasks;
using MotorcycleRAG.MobileApp.Persistence.Entities;

namespace MotorcycleRAG.MobileApp.Persistence.Repositories;

public interface IConversationRepository
{
    Task<List<ConversationEntity>> GetAllAsync();
    Task<ConversationEntity?> GetByIdAsync(string id);
    Task<List<ConversationEntity>> SearchAsync(string query);
    Task<int> InsertAsync(ConversationEntity conversation);
    Task<int> UpdateAsync(ConversationEntity conversation);
    Task<int> DeleteAsync(string id);
    Task<long> GetTotalStorageSizeAsync();
    Task PruneOldestConversationsAsync(long bytesToFree);
}
