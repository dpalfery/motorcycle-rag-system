using System.Threading.Tasks;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Contracts.Interfaces {
    public interface IDataProcessor<T> {
        Task<ProcessedData> ProcessAsync(T input);
    }
}
