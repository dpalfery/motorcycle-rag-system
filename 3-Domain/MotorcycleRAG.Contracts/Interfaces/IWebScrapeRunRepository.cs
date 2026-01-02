using System.Threading.Tasks;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Repository interface for web scrape run management
/// </summary>
public interface IWebScrapeRunRepository
{
    /// <summary>
    /// Creates a new web scrape run record
    /// </summary>
    /// <param name="webSourceId">Web source ID</param>
    /// <returns>Created web scrape run ID</returns>
    Task<long> CreateWebScrapeRunAsync(int webSourceId);

    /// <summary>
    /// Updates a web scrape run with completion status
    /// </summary>
    /// <param name="runId">Run ID</param>
    /// <param name="status">Final status</param>
    /// <param name="pagesCrawled">Number of pages crawled</param>
    /// <param name="pagesIndexed">Number of pages indexed</param>
    /// <param name="errors">Number of errors encountered</param>
    /// <param name="errorMessage">Error message if failed</param>
    /// <returns>True if successful, false otherwise</returns>
    Task<bool> UpdateWebScrapeRunAsync(
        long runId,
        ScrapeRunStatus status,
        int pagesCrawled,
        int pagesIndexed,
        int errors,
        string? errorMessage = null);

    /// <summary>
    /// Gets scrape run by ID
    /// </summary>
    /// <param name="runId">Run ID</param>
    /// <returns>Web scrape run if found, null otherwise</returns>
    Task<WebScrapeRun?> GetWebScrapeRunAsync(long runId);

    /// <summary>
    /// Gets recent scrape runs for a web source
    /// </summary>
    /// <param name="webSourceId">Web source ID</param>
    /// <param name="limit">Maximum number of runs to return</param>
    /// <returns>List of recent scrape runs</returns>
    Task<WebScrapeRun[]> GetRecentScrapeRunsAsync(int webSourceId, int limit = 10);

    /// <summary>
    /// Gets all pending or running scrape runs
    /// </summary>
    /// <returns>List of active scrape runs</returns>
    Task<WebScrapeRun[]> GetActiveScrapeRunsAsync();
}
