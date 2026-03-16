using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Application.Agents;

public class QueryPlannerAgent : IQueryPlannerAgent
{
    private readonly ILogger<QueryPlannerAgent> _logger;

    public QueryPlannerAgent(ILogger<QueryPlannerAgent> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public SearchAgentType AgentType => SearchAgentType.QueryPlanner;

    public async Task<SearchResult[]> SearchAsync(string query, SearchParameters options)
    {
        _logger.LogInformation("QueryPlannerAgent executing search for: {Query}", query);
        return await Task.FromResult(Array.Empty<SearchResult>());
    }

    public Task<string> PlanQueryAsync(string userQuery)
    {
        throw new NotImplementedException("QueryPlannerAgent is superseded by Foundry-hosted OrchestratorAgent");
    }
}
