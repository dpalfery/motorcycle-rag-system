using MotorcycleRAG.Contracts.Models.DTOs;
using System.Threading;
using System.Threading.Tasks;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Interface for Azure AI Search health checks and connection management
/// </summary>
public interface IAzureSearchHealthService
{
    /// <summary>
    /// Checks if the Azure Search service is healthy
    /// </summary>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a basic search for health check purposes
    /// </summary>
    Task<SearchResult[]> SearchAsync(string searchText, int maxResults, CancellationToken cancellationToken);
}