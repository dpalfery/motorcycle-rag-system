using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.Application.Services.Web;

/// <summary>
/// Enhances search queries with motorcycle-specific search terms.
/// AI-based term generation removed; search terms are now refined by Foundry-hosted agents.
/// </summary>
public class WebSearchTermEnhancer
{
    private readonly ILogger<WebSearchTermEnhancer> _logger;

    public WebSearchTermEnhancer(
        IOptions<WebSearchOptions> options,
        ILogger<WebSearchTermEnhancer> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public WebSearchTermEnhancer(ILogger<WebSearchTermEnhancer> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public Task<List<string>> GenerateSearchTermsAsync(
        string originalQuery,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Using original query as search term (Foundry agents handle term refinement)");
        return Task.FromResult(new List<string> { originalQuery });
    }

    public Task<string> EnhanceSearchTermsAsync(string query) =>
        Task.FromResult(query);
}
