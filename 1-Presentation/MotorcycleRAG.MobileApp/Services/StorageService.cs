using System.Threading.Tasks;
using MotorcycleRAG.MobileApp.Persistence.Repositories;

namespace MotorcycleRAG.MobileApp.Services;

public class StorageService : IStorageService
{
    private readonly IConversationRepository _conversationRepository;
    private const long MaxStorageBytes = 100 * 1024 * 1024; // 100 MB

    public StorageService(IConversationRepository conversationRepository)
    {
        _conversationRepository = conversationRepository;
    }

    public async Task InitializeAsync()
    {
        // Any initialization if needed
        await PruneIfNeededAsync();
    }

    public async Task<long> GetUsageBytesAsync()
    {
        return await _conversationRepository.GetTotalStorageSizeAsync();
    }

    public async Task PruneIfNeededAsync()
    {
        var currentUsage = await GetUsageBytesAsync();
        if (currentUsage > MaxStorageBytes)
        {
            var bytesToFree = currentUsage - MaxStorageBytes;
            // Add a buffer to avoid frequent pruning, e.g., free 10% extra
            bytesToFree += (long)(MaxStorageBytes * 0.1);

            await _conversationRepository.PruneOldestConversationsAsync(bytesToFree);
        }
    }
}
