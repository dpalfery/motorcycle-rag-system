using System.Collections.Generic;
using System.Threading.Tasks;
using SQLite;
using MotorcycleRAG.MobileApp.Persistence.Entities;

namespace MotorcycleRAG.MobileApp.Persistence.Repositories;

public class CitationRepository : ICitationRepository
{
    private readonly SQLiteAsyncConnection _database;

    public CitationRepository(SQLiteAsyncConnection database)
    {
        _database = database;
        _database.CreateTableAsync<CitationEntity>().Wait();
    }

    public async Task<List<CitationEntity>> GetByMessageIdAsync(string messageId)
    {
        return await _database.Table<CitationEntity>()
            .Where(c => c.MessageId == messageId)
            .ToListAsync();
    }

    public async Task<int> InsertManyAsync(List<CitationEntity> citations)
    {
        return await _database.InsertAllAsync(citations);
    }

    public async Task<int> DeleteByMessageIdAsync(string messageId)
    {
        var citations = await GetByMessageIdAsync(messageId);
        int count = 0;
        foreach (var c in citations)
        {
            count += await _database.DeleteAsync(c);
        }
        return count;
    }
}
