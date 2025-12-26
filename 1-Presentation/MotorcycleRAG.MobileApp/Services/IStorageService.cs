using System.Threading.Tasks;

namespace MotorcycleRAG.MobileApp.Services;

public interface IStorageService
{
    Task InitializeAsync();
    Task<long> GetUsageBytesAsync();
    Task PruneIfNeededAsync();
}
