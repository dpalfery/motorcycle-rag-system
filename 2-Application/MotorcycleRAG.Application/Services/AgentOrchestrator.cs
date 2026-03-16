using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Application.Services;

/// <summary>
/// Coordinates search agents and delegates LLM orchestration to Azure AI Foundry Agent Service.
/// Execution of the sequential retrieval policy and response generation will be implemented
/// in Phase 3 using <see cref="IFoundryAgentRunner"/>.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S1200:Split this class into smaller and more specialized ones", Justification = "Orchestrator naturally depends on multiple service types")]
public sealed class AgentOrchestrator : IAgentOrchestrator
{
    private readonly IReadOnlyList<ISearchAgent> _agents;
    private readonly ILogger<AgentOrchestrator> _logger;

    public AgentOrchestrator(
        IEnumerable<ISearchAgent> agents,
        ILogger<AgentOrchestrator> logger)
    {
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(logger);

        _agents = agents.ToList();
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<SearchResult[]> ExecuteSequentialSearchAsync(string query, SearchContext context)
    {
        throw new NotImplementedException(
            "ExecuteSequentialSearchAsync will be implemented in Phase 3 via IFoundryAgentRunner");
    }

    /// <inheritdoc />
    public Task<string> GenerateResponseAsync(SearchResult[] results, string originalQuery)
    {
        throw new NotImplementedException(
            "GenerateResponseAsync will be implemented in Phase 3 via IFoundryAgentRunner");
    }

    /// <inheritdoc />
    public Task<SearchResult[]> OrchestrateSearchAsync(string query, SearchParameters options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(query))
        {
            _logger.LogWarning("OrchestrateSearchAsync was invoked with an empty query");
            return Task.FromResult(Array.Empty<SearchResult>());
        }

        var context = new SearchContext
        {
            Preferences = new SearchPreferences
            {
                MaxResults = options.MaxResults,
                MinRelevanceScore = options.MinRelevanceScore
            }
        };

        return ExecuteSequentialSearchAsync(query, context);
    }

    /// <inheritdoc />
    public IEnumerable<ISearchAgent> GetAvailableAgents() => _agents;
}
