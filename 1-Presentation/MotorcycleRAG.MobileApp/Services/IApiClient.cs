using System.Threading.Tasks;
using MotorcycleRAG.MobileApp.Models;

namespace MotorcycleRAG.MobileApp.Services;

public interface IApiClient
{
    Task<QueryResponse> QueryAsync(QueryRequest request);
    Task<UserProfile> GetUserProfileAsync();
}
