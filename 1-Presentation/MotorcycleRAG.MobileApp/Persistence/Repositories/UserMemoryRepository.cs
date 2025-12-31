using System.Collections.Generic;
using System.Threading.Tasks;
using SQLite;
using MotorcycleRAG.MobileApp.Persistence.Entities;

namespace MotorcycleRAG.MobileApp.Persistence.Repositories;

public class UserMemoryRepository : IUserMemoryRepository
{
    private readonly SQLiteAsyncConnection _database;

    public UserMemoryRepository(SQLiteAsyncConnection database)
    {
        _database = database;
    }

    public async Task<List<UserMemoryEntity>> GetActiveMemoriesAsync()
    {
        return await _database.Table<UserMemoryEntity>()
            .Where(m => m.IsActive == 1)
            .ToListAsync();
    }

    public async Task<UserMemoryEntity?> GetByCategoryAsync(string category)
    {
        return await _database.Table<UserMemoryEntity>()
            .Where(m => m.Category == category && m.IsActive == 1)
            .FirstOrDefaultAsync();
    }

    public async Task<int> InsertAsync(UserMemoryEntity memory)
    {
        return await _database.InsertAsync(memory);
    }

    public async Task<int> UpdateAsync(UserMemoryEntity memory)
    {
        return await _database.UpdateAsync(memory);
    }

    public async Task<int> DeactivateAsync(string id)
    {
        var memory = await _database.Table<UserMemoryEntity>().Where(m => m.Id == id).FirstOrDefaultAsync();
        if (memory != null)
        {
            memory.IsActive = 0;
            return await _database.UpdateAsync(memory);
        }
        return 0;
    }

    public async Task<int> DeleteAsync(string id)
    {
        return await _database.DeleteAsync<UserMemoryEntity>(id);
    }
}
