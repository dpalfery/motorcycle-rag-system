using System.Collections.Generic;
using System.Threading.Tasks;
using SQLite;
using MotorcycleRAG.MobileApp.Persistence.Entities;
using System.IO;
using System;

namespace MotorcycleRAG.MobileApp.Persistence.Repositories;

public class ConversationRepository : IConversationRepository
{
    private readonly SQLiteAsyncConnection _database;

    public ConversationRepository(SQLiteAsyncConnection database)
    {
        _database = database;
    }

    public async Task<List<ConversationEntity>> GetAllAsync()
    {
        return await _database.Table<ConversationEntity>()
            .OrderByDescending(c => c.UpdatedAt)
            .ToListAsync();
    }

    public async Task<ConversationEntity?> GetByIdAsync(string id)
    {
        return await _database.Table<ConversationEntity>()
            .Where(c => c.Id == id)
            .FirstOrDefaultAsync();
    }

    public async Task<List<ConversationEntity>> SearchAsync(string query)
    {
        // Simple LIKE search for now as FTS requires more setup
        return await _database.Table<ConversationEntity>()
            .Where(c => c.Title.Contains(query))
            .OrderByDescending(c => c.UpdatedAt)
            .ToListAsync();
    }

    public async Task<int> InsertAsync(ConversationEntity conversation)
    {
        return await _database.InsertAsync(conversation);
    }

    public async Task<int> UpdateAsync(ConversationEntity conversation)
    {
        return await _database.UpdateAsync(conversation);
    }

    public async Task<int> DeleteAsync(string id)
    {
        return await _database.DeleteAsync<ConversationEntity>(id);
    }

    public async Task<long> GetTotalStorageSizeAsync()
    {
        // This should be a SUM query, but sqlite-net-pcl basic usage:
        var all = await _database.Table<ConversationEntity>().ToListAsync();
        long sum = 0;
        foreach (var c in all) sum += c.SizeBytes;
        return sum;
    }

    public async Task PruneOldestConversationsAsync(long bytesToFree)
    {
        // Logic to find oldest and delete until bytesToFree is met
        // This requires coordination with MessageRepository to actually free space
        // For MVP, just deleting the entity
        var oldest = await _database.Table<ConversationEntity>()
            .OrderBy(c => c.UpdatedAt)
            .ToListAsync();

        long freed = 0;
        foreach (var c in oldest)
        {
            if (freed >= bytesToFree) break;
            await DeleteAsync(c.Id);
            freed += c.SizeBytes;
        }
    }
}
