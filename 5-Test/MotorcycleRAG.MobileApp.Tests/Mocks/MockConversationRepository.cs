using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using MotorcycleRAG.MobileApp.Persistence.Entities;
using MotorcycleRAG.MobileApp.Persistence.Repositories;

namespace MotorcycleRAG.MobileApp.Tests.Mocks;

public class MockConversationRepository : IConversationRepository
{
    public Collection<ConversationEntity> Conversations { get; } = new();

    public Task<List<ConversationEntity>> GetAllAsync()
    {
        return Task.FromResult(Conversations.OrderByDescending(c => c.UpdatedAt).ToList());
    }

    public Task<ConversationEntity?> GetByIdAsync(string id)
    {
        return Task.FromResult(Conversations.FirstOrDefault(c => c.Id == id));
    }

    public Task<List<ConversationEntity>> SearchAsync(string query)
    {
        return Task.FromResult(Conversations
            .Where(c => c.Title.Contains(query))
            .OrderByDescending(c => c.UpdatedAt)
            .ToList());
    }

    public Task<int> InsertAsync(ConversationEntity conversation)
    {
        Conversations.Add(conversation);
        return Task.FromResult(1);
    }

    public Task<int> UpdateAsync(ConversationEntity conversation)
    {
        var existing = Conversations.FirstOrDefault(c => c.Id == conversation.Id);
        if (existing != null)
        {
            Conversations.Remove(existing);
            Conversations.Add(conversation);
            return Task.FromResult(1);
        }
        return Task.FromResult(0);
    }

    public Task<int> DeleteAsync(string id)
    {
        var existing = Conversations.FirstOrDefault(c => c.Id == id);
        if (existing != null)
        {
            Conversations.Remove(existing);
            return Task.FromResult(1);
        }
        return Task.FromResult(0);
    }

    public Task<long> GetTotalStorageSizeAsync()
    {
        return Task.FromResult(Conversations.Sum(c => c.SizeBytes));
    }

    public Task PruneOldestConversationsAsync(long bytesToFree)
    {
        var sorted = Conversations.OrderBy(c => c.UpdatedAt).ToList();
        long freed = 0;
        foreach (var c in sorted)
        {
            if (freed >= bytesToFree) break;
            Conversations.Remove(c);
            freed += c.SizeBytes;
        }
        return Task.CompletedTask;
    }
}
