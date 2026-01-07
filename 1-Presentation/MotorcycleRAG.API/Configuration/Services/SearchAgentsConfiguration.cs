using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Application.Agents;
using MotorcycleRAG.Core.Options;

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

        // Register WebSearchAgent with optional IWebTrustPolicyStore for trust tier filtering
        services.AddScoped<ISearchAgent>(provider => {
            var httpClient = provider.GetRequiredService<HttpClient>();
            var openAIClient = provider.GetRequiredService<IAzureOpenAIClient>();
            var config = provider.GetRequiredService<IOptions<WebSearchOptions>>();
            var logger = provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<WebSearchAgent>>();
            var trustPolicyStore = provider.GetService<IWebTrustPolicyStore>();

            return new MotorcycleRAG.Application.Agents.WebSearchAgent(httpClient, openAIClient, config, logger, trustPolicyStore);
        });

        services.AddScoped<IQueryPlannerAgent, MotorcycleRAG.Application.Agents.QueryPlannerAgent>();

        return services;
    }
}
