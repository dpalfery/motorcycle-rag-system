using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Application.Agents;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Application.Services.Web;

namespace MotorcycleRAG.API.Configuration.Services;

/// <summary>
/// Configuration for search agents
/// </summary>
internal static class SearchAgentsConfiguration
{
    /// <summary>
    /// Configure search agents
    /// </summary>
    internal static IServiceCollection AddSearchAgents(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure search options
        services.Configure<SearchOptions>(configuration.GetSection("Search"));

        // Register search agent implementations from Application layer
        services.AddScoped<ISearchAgent, MotorcycleRAG.Application.Agents.VectorSearchAgent>();

        // Register Web search services (extracted from WebSearchAgent)
        services.AddSingleton<WebSearchRateLimiter>(sp =>
        {
            var config = sp.GetRequiredService<IOptions<WebSearchOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<WebSearchRateLimiter>>();
            return new WebSearchRateLimiter(
                config.MaxConcurrentRequests,
                config.MinRequestIntervalMs,
                logger);
        });

        services.AddSingleton<WebSearchCache>(sp =>
        {
            var config = sp.GetRequiredService<IOptions<WebSearchOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<WebSearchCache>>();
            return new WebSearchCache(
                TimeSpan.FromHours(1),
                maxCacheSize: 100,
                logger);
        });

        services.AddScoped<MotorcycleRAG.Application.Services.Web.WebContentExtractor>();
        services.AddScoped<WebSourceValidator>();
        services.AddScoped<WebSearchTermEnhancer>();

        // Register WebSearchAgent with extracted services
        services.AddScoped<ISearchAgent>(provider => {
            var httpClient = provider.GetRequiredService<HttpClient>();
            var config = provider.GetRequiredService<IOptions<WebSearchOptions>>();
            var logger = provider.GetRequiredService<ILogger<WebSearchAgent>>();
            
            // Inject extracted services
            var rateLimiter = provider.GetRequiredService<WebSearchRateLimiter>();
            var cache = provider.GetRequiredService<WebSearchCache>();
            var contentExtractor = provider.GetRequiredService<MotorcycleRAG.Application.Services.Web.WebContentExtractor>();
            var validator = provider.GetRequiredService<WebSourceValidator>();
            var termEnhancer = provider.GetRequiredService<WebSearchTermEnhancer>();

            return new MotorcycleRAG.Application.Agents.WebSearchAgent(
                httpClient,
                config,
                logger,
                rateLimiter,
                cache,
                contentExtractor,
                termEnhancer,
                validator);
        });

        services.AddScoped<IQueryPlannerAgent, MotorcycleRAG.Application.Agents.QueryPlannerAgent>();

        return services;
    }
}
