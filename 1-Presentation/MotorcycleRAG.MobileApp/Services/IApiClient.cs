using System.Threading.Tasks;
using MotorcycleRAG.MobileApp.Models;

namespace MotorcycleRAG.MobileApp.Services;

public interface IApiClient
{
    Task<ChatMessage> SendQueryAsync(string query, string conversationId, List<string> previousQueries);
}
