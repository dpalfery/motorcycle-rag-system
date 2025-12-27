using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;
using MotorcycleRAG.MobileApp.Models;
using MotorcycleRAG.MobileApp.Persistence.Repositories;

namespace MotorcycleRAG.MobileApp.Services;

public class StorageService : IStorageService
{
    private readonly IConversationRepository _conversationRepository;
    private readonly IMessageRepository _messageRepository;
    private readonly IUserMemoryService _userMemoryService;
    private const long MaxStorageBytes = 100 * 1024 * 1024; // 100 MB

    public StorageService(
        IConversationRepository conversationRepository,
        IMessageRepository messageRepository,
        IUserMemoryService userMemoryService)
    {
        _conversationRepository = conversationRepository;
        _messageRepository = messageRepository;
        _userMemoryService = userMemoryService;
    }

    public string GetCacheDirectory()
    {
        return FileSystem.CacheDirectory;
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

            var allConversations = await _conversationRepository.GetAllAsync();
            var oldestConversations = allConversations.OrderBy(c => DateTime.Parse(c.UpdatedAt)).ToList();

            long freed = 0;
            foreach (var conversation in oldestConversations)
            {
                if (freed >= bytesToFree) break;

                // Extract memory before deleting
                var messages = await _messageRepository.GetByConversationIdAsync(conversation.Id);
                foreach (var message in messages)
                {
                    if (message.Sender == (int)MessageSender.User)
                    {
                        await _userMemoryService.ExtractFromConversationAsync(conversation.Id, message.Content);
                    }
                }

                await _conversationRepository.DeleteAsync(conversation.Id);
                freed += conversation.SizeBytes;
            }
        }
    }
}
