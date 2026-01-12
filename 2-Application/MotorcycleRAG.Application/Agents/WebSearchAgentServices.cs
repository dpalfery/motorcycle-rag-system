using MotorcycleRAG.Application.Services.Web;
using ServiceWebContentExtractor = MotorcycleRAG.Application.Services.Web.WebContentExtractor;
namespace MotorcycleRAG.Application.Agents;

/// <summary>
/// Groups WebSearchAgent dependencies to keep the agent constructor small.
/// </summary>
public sealed class WebSearchAgentServices
{
    public WebSearchRateLimiter RateLimiter { get; }
    public WebSearchCache Cache { get; }
    public ServiceWebContentExtractor Extractor { get; }
    public WebSearchTermEnhancer TermEnhancer { get; }
    public WebSourceValidator Validator { get; }

    public WebSearchAgentServices(
        WebSearchRateLimiter rateLimiter,
        WebSearchCache cache,
        ServiceWebContentExtractor extractor,
        WebSearchTermEnhancer termEnhancer,
        WebSourceValidator validator)
    {
        RateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
        Cache = cache ?? throw new ArgumentNullException(nameof(cache));
        Extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
        TermEnhancer = termEnhancer ?? throw new ArgumentNullException(nameof(termEnhancer));
        Validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }
}


