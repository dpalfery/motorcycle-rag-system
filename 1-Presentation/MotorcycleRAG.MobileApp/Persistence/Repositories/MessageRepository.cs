using System.Collections.Generic;
using System.Threading.Tasks;
using SQLite;
using MotorcycleRAG.MobileApp.Persistence.Entities;

namespace MotorcycleRAG.MobileApp.Persistence.Repositories;

public class MessageRepository : IMessageRepository
{
    private readonly SQLiteAsyncConnection _database;

    public MessageRepository(SQLiteAsyncConnection database)
    {
        _database = database;
        _database.CreateTableAsync<MessageEntity>().Wait();
    }

    public async Task<List<MessageEntity>> GetByConversationIdAsync(string conversationId)
    {
        return await _database.Table<MessageEntity>()
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.Timestamp)
            .ToListAsync();
    }

    public async Task<int> InsertAsync(MessageEntity message)
    {
        return await _database.InsertAsync(message);
    }

    public async Task<int> UpdateAsync(MessageEntity message)
    {
        return await _database.UpdateAsync(message);
    }

    public async Task<int> DeleteByConversationIdAsync(string conversationId)
    {
        // Delete all messages for a conversation
        var messages = await GetByConversationIdAsync(conversationId);
        int count = 0;
        foreach (var msg in messages)
        {
            count += await _database.DeleteAsync(msg);
        }
        return count;
    }
}
