using System.Threading.Tasks;
using MotorcycleRAG.Contracts.Models;
using MotorcycleRAG.Domain.Models;

namespace MotorcycleRAG.Contracts.Interfaces
{
    public interface IDataProcessor<T>
    {
        Task<ProcessedData> ProcessAsync(T input);
    }
}
