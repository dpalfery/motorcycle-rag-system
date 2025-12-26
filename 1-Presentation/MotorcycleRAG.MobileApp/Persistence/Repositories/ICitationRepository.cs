using System.Collections.Generic;
using System.Threading.Tasks;
using MotorcycleRAG.MobileApp.Persistence.Entities;

namespace MotorcycleRAG.MobileApp.Persistence.Repositories;

public interface ICitationRepository
{
    Task<List<CitationEntity>> GetByMessageIdAsync(string messageId);
    Task<int> InsertManyAsync(List<CitationEntity> citations);
    Task<int> DeleteByMessageIdAsync(string messageId);
}
