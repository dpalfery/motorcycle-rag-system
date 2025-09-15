using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Models;
using MotorcycleRAG.Shared.Configuration;

namespace MotorcycleRAG.Application.Agents;

public class QueryPlannerAgent : IQueryPlannerAgent
{
    private readonly IAzureOpenAIClient _openAIClient;
    private readonly IEnumerable<ISearchAgent> _searchAgents;
    private readonly ModelConfiguration _modelConfig;
    private readonly ILogger<QueryPlannerAgent> _logger;

    public SearchAgentType AgentType => SearchAgentType.QueryPlanner;

    public QueryPlannerAgent(
        IAzureOpenAIClient openAIClient,
        IEnumerable<ISearchAgent> searchAgents,
        IOptions<AzureAIConfiguration> azureConfig,
        ILogger<QueryPlannerAgent> logger)
    {
        _openAIClient = openAIClient ?? throw new ArgumentNullException(nameof(openAIClient));
        _searchAgents = searchAgents ?? throw new ArgumentNullException(nameof(searchAgents));
        _modelConfig = azureConfig?.Value.Models ?? throw new ArgumentNullException(nameof(azureConfig));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // Interface implementation (no CancellationToken per interface)
    public Task<QueryPlan> GeneratePlanAsync(string query, SearchOptions options) =>
        GeneratePlanInternalAsync(query, options, CancellationToken.None);

    public Task<SearchResult[]> SearchAsync(string query, SearchOptions options) =>
        SearchInternalAsync(query, options, CancellationToken.None);

    // Internal overloads supporting cancellation if needed later
    private async Task<SearchResult[]> SearchInternalAsync(string query, SearchOptions options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            _logger.LogWarning("Empty query provided to QueryPlannerAgent");
            return Array.Empty<SearchResult>();
        }

        var plan = await GeneratePlanInternalAsync(query, options, cancellationToken);

        if (plan.SubQueries == null || plan.SubQueries.Count == 0)
        {
            return Array.Empty<SearchResult>();
        }

        var tasks = new List<Task<SearchResult[]>>();
        var vectorAgent = _searchAgents.FirstOrDefault(a => a.AgentType == SearchAgentType.VectorSearch);
        var webAgent = plan.UseWebSearch
            ? _searchAgents.FirstOrDefault(a => a.AgentType == SearchAgentType.WebSearch)
            : null;

        foreach (var subQuery in plan.SubQueries)
        {
            if (vectorAgent != null)
            {
                tasks.Add(vectorAgent.SearchAsync(subQuery, options));
            }

            if (webAgent != null)
            {
                tasks.Add(webAgent.SearchAsync(subQuery, options));
            }
        }

        if (tasks.Count == 0)
        {
            return Array.Empty<SearchResult>();
        }

        SearchResult[][] results;
        if (plan.RunParallel)
        {
            results = await Task.WhenAll(tasks);
        }
        else
        {
            results = new SearchResult[tasks.Count][];
            for (var i = 0; i < tasks.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results[i] = await tasks[i];
            }
        }

        return results.SelectMany(r => r).ToArray();
    }

    private async Task<QueryPlan> GeneratePlanInternalAsync(string query, SearchOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var prompt = BuildPlanningPrompt(query);
            var response = await _openAIClient.GetChatCompletionAsync(
                _modelConfig.QueryPlannerModel,
                prompt,
                cancellationToken);
            var plan = JsonSerializer.Deserialize<QueryPlan>(
                response,
                JsonSerializationConfiguration.DefaultOptions);
            if (plan?.SubQueries == null || plan.SubQueries.Count == 0)
            {
                plan = CreateFallbackPlan(query);
            }

            plan.OriginalQuery = query;
            return plan;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Query plan generation canceled for query: {Query}", query);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate query plan; using fallback plan");
            return CreateFallbackPlan(query);
        }
    }

    private static string BuildPlanningPrompt(string query) =>
        @"You are a motorcycle search query planner." +
        " Break the user question into 1-3 focused search queries and determine if web search is needed." +
        " Return JSON with fields 'subQueries', 'useWebSearch', and 'runParallel'." +
        $" User question: \"{query}\"";

    private static QueryPlan CreateFallbackPlan(string query) => new()
    {
        OriginalQuery = query,
        SubQueries = new List<string> { query },
        UseWebSearch = true,
        RunParallel = true
    };
}

