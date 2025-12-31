using System.Collections.Generic;
using System.Threading.Tasks;
using MotorcycleRAG.MobileApp.Persistence.Entities;

namespace MotorcycleRAG.MobileApp.Persistence.Repositories;

public interface IUserMemoryRepository
{
    Task<List<UserMemoryEntity>> GetActiveMemoriesAsync();
    Task<UserMemoryEntity?> GetByCategoryAsync(string category);
    Task<int> InsertAsync(UserMemoryEntity memory);
    Task<int> UpdateAsync(UserMemoryEntity memory);
    Task<int> DeactivateAsync(string id);
    Task<int> DeleteAsync(string id);
}
