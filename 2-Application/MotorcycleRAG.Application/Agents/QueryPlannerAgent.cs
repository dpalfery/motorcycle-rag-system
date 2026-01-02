using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.DTOs;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.Application.Agents;

public class QueryPlannerAgent : IQueryPlannerAgent
{
    private readonly IAzureOpenAIClient _openAIClient;
    private readonly ILogger<QueryPlannerAgent> _logger;
    private readonly AzureAIOptions _aiOptions;

    public QueryPlannerAgent(
        IAzureOpenAIClient openAIClient,
        IOptions<AzureAIOptions> aiOptions,
        ILogger<QueryPlannerAgent> logger)
    {
        _openAIClient = openAIClient ?? throw new ArgumentNullException(nameof(openAIClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _aiOptions = aiOptions?.Value ?? throw new ArgumentNullException(nameof(aiOptions));
    }

    public SearchAgentType AgentType => SearchAgentType.QueryPlanner;

    public async Task<SearchResult[]> SearchAsync(string query, SearchParameters options)
    {
        _logger.LogInformation("QueryPlannerAgent executing search for: {Query}", query);
        return await Task.FromResult(Array.Empty<SearchResult>());
    }

    public async Task<string> PlanQueryAsync(string userQuery)
    {
        var prompt = $"Plan a search strategy for the following query: {userQuery}";
        return await _openAIClient.GetChatCompletionAsync(_aiOptions.Models.QueryPlannerModel, prompt, CancellationToken.None);
    }
}
