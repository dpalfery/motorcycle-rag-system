using System.Collections.Generic;
using System.Threading.Tasks;
using MotorcycleRAG.MobileApp.Models;

namespace MotorcycleRAG.MobileApp.Services
{
    public interface IUserMemoryService
    {
        Task<List<UserMemory>> GetActiveMemoriesAsync();
        Task ExtractFromConversationAsync(string conversationId, string messageContent);
        Task<UserMemory> AddMemoryAsync(string category, string value, string sourceConversationId);
        Task UpdateMemoryAsync(UserMemory memory);
        Task DeactivateMemoryAsync(string id);
        Task DeleteMemoryAsync(string id);
        Task ClearAllMemoriesAsync();
    }
}
