using System.Threading;
using System.Threading.Tasks;
using MotorcycleRAG.Domain.Entities;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Interface for orchestrating web scraping and indexing operations
/// </summary>
public interface IWebScrapeOrchestrator
{
    /// <summary>
    /// Starts a new web scrape run for a web source
    /// </summary>
    /// <param name="webSourceId">The ID of the web source to scrape</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The ID of the created scrape run</returns>
    Task<long> StartScrapeRunAsync(int webSourceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a running scrape operation
    /// </summary>
    /// <param name="runId">The ID of the scrape run to cancel</param>
    /// <returns>True if cancellation was successful, false otherwise</returns>
    Task<bool> CancelScrapeRunAsync(long runId);

    /// <summary>
    /// Gets the current status of a scrape run
    /// </summary>
    /// <param name="runId">The ID of the scrape run</param>
    /// <returns>The scrape run entity if found, null otherwise</returns>
    Task<WebScrapeRun?> GetScrapeRunStatusAsync(long runId);

    /// <summary>
    /// Gets recent scrape runs for a web source
    /// </summary>
    /// <param name="webSourceId">The ID of the web source</param>
    /// <param name="limit">Maximum number of runs to return (default 10)</param>
    /// <returns>Array of recent scrape runs</returns>
    Task<WebScrapeRun[]> GetRecentScrapeRunsAsync(int webSourceId, int limit = 10);

    /// <summary>
    /// Gets all currently active/running scrape operations
    /// </summary>
    /// <returns>Array of active scrape runs</returns>
    Task<WebScrapeRun[]> GetActiveScrapeRunsAsync();
}
