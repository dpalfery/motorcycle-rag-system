using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Application.Agents;
using MotorcycleRAG.Application.Services.Telemetry;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.Application.Services;

/// <summary>
/// Coordinates multiple search agents using the Microsoft Agent Framework to rank and fuse their results.
/// Integrates MCP (Model Context Protocol) tool configuration for extensible tool management.
/// Implements partial-results aggregation for graceful degradation when sources become unavailable.
/// </summary>
public sealed class AgentOrchestrator : IAgentOrchestrator
{
    private readonly IReadOnlyList<ISearchAgent> _agents;
    private readonly IAzureOpenAIClient _openAIClient;
    private readonly ILogger<AgentOrchestrator> _logger;
    private readonly AgentFrameworkAdapter _frameworkAdapter;
    private readonly AgentState _executionState;
    private readonly MotorcycleRAG.Application.Services.Mcp.McpToolManager _mcpToolManager;
    private readonly MotorcycleRAG.Application.Services.Telemetry.DegradedModeTracker _degradedModeTracker;
    private readonly SearchResultFusionService _resultFusionService;

    public AgentOrchestrator(
        IEnumerable<ISearchAgent> agents,
        ILogger<AgentOrchestrator> logger,
        AgentOrchestratorDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(dependencies);

        _agents = agents.ToList();
        _logger = logger;

        _openAIClient = dependencies.OpenAIClient;
        _mcpToolManager = dependencies.McpToolManager;
        _degradedModeTracker = dependencies.DegradedModeTracker;
        _resultFusionService = dependencies.ResultFusionService;

        _frameworkAdapter = new AgentFrameworkAdapter(logger);
        _executionState = new AgentState();

        InitializeFrameworkAdapter();
        _ = InitializeMcpToolsAsync();
    }

    /// <summary>
    /// Initialize the Agent Framework adapter with registered agents
    /// </summary>
    private void InitializeFrameworkAdapter()
    {
        foreach (var agent in _agents)
        {
            var toolName = agent.AgentType switch
            {
                SearchAgentType.VectorSearch => "vector_search",
                SearchAgentType.WebSearch => "web_search",
                SearchAgentType.PDFSearch => "pdf_search",
                SearchAgentType.QueryPlanner => "plan_search_strategy",
                _ => $"agent_{agent.AgentType.ToString().ToUpperInvariant()}"
            };

            var handler = AgentFrameworkAdapter.CreateSearchAgentHandler(agent, _logger);
            _frameworkAdapter.RegisterToolHandler(toolName, handler);
        }

        _logger.LogInformation("Agent Framework adapter initialized with {AgentCount} agents", _agents.Count);
    }

    /// <summary>
    /// Initialize MCP tool configurations
    /// </summary>
    private async Task InitializeMcpToolsAsync()
    {
        try
        {
            await _mcpToolManager.InitializeAsync();
            _logger.LogInformation("MCP tools initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize MCP tools on startup - will retry later");
        }
    }

    /// <summary>
    /// Get enabled MCP tools for current execution
    /// </summary>
    private Task<McpToolConfiguration[]> GetEnabledMcpToolsAsync() => _mcpToolManager.GetEnabledToolsAsync();

    #region IAgentOrchestrator Implementation

    /// <inheritdoc />
    public async Task<SearchResult[]> ExecuteSequentialSearchAsync(string query, SearchContext context)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            _logger.LogWarning("ExecuteSequentialSearchAsync was invoked with an empty query");
            return Array.Empty<SearchResult>();
        }

        context ??= new SearchContext();

        _executionState.OriginalQuery = query;
        _executionState.SearchContext = context;
        _executionState.Status = AgentExecutionStatus.Running;

        try
        {
            // Load enabled MCP tools for this execution
            var enabledMcpTools = await GetEnabledMcpToolsAsync();
            if (enabledMcpTools.Length > 0)
            {
                _logger.LogInformation(
                    "Executing search with {McpToolCount} enabled MCP tools: {Tools}",
                    enabledMcpTools.Length,
                    string.Join(", ", enabledMcpTools.Select(t => t.ToolId)));
            }

            // Use the sequential retrieval policy: index → web → pdf fallback
            var results = await ExecuteSequentialRetrievalPolicyAsync(query, context);

            _executionState.MarkComplete();
            return results;
        }
        catch (Exception ex)
        {
            _executionState.MarkFailed();
            _logger.LogError(ex, "Sequential search execution failed");
            throw new InvalidOperationException("Sequential search execution failed", ex);
        }
    }

    /// <inheritdoc />
    public async Task<string> GenerateResponseAsync(SearchResult[] results, string originalQuery)
    {
        if (results == null || results.Length == 0)
        {
            _logger.LogWarning("GenerateResponseAsync called with no results – returning empty response.");
            return string.Empty;
        }

        try
        {
            _logger.LogInformation("Generating response for query: {Query}", originalQuery);

            var snippets = results.Take(10)
                .Select(r => $"[{r.Id}] {Truncate(r.Content, 500)}")
                .ToArray();

            var prompt = $"""
User question: \"{originalQuery}\"

Snippets:
{string.Join("\n\n", snippets)}

Answer in markdown:
""";

            var answer = await _openAIClient.GetChatCompletionAsync("gpt-4o-mini", prompt, CancellationToken.None);

            _executionState.AddMessage(
                "ResponseGenerator",
                $"Generated response of {answer.Length} characters",
                AgentMessageType.FunctionReturn);

            _logger.LogInformation("Response generated successfully");
            return answer;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate response via OpenAI");
            _executionState.RecordError("ResponseGenerator", ex.Message, ex);
            throw new InvalidOperationException("Failed to generate response via OpenAI", ex);
        }
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

    #endregion

    #region Sequential Execution Logic

    /// <summary>
    /// Executes sequential retrieval policy: index → web → pdf fallback.
    /// Implements partial-results aggregation with degraded-mode tracking.
    /// When sources fail, continues with remaining available sources and tracks the degradation.
    /// </summary>
    private async Task<SearchResult[]> ExecuteSequentialRetrievalPolicyAsync(string query, SearchContext context)
    {
        var searchParameters = BuildSearchOptions(context);
        var aggregatedResults = new List<SearchResult>();
        var sourceStatuses = new List<SourceExecutionStatus>();
        var executionMetrics = new Dictionary<SearchAgentType, (TimeSpan Duration, int ResultsFound)>();

        // Define the execution order: VectorSearch (index) → WebSearch → PDFSearch (fallback)
        var executionOrder = new[] { SearchAgentType.VectorSearch, SearchAgentType.WebSearch, SearchAgentType.PDFSearch };

        var stopwatch = Stopwatch.StartNew();

        foreach (var agentType in executionOrder)
        {
            var (results, elapsed) = await ExecuteAgentSearchWithMetricsAsync(agentType, query, searchParameters);

            if (results != null)
            {
                executionMetrics[agentType] = (elapsed, results.Length);
                aggregatedResults.AddRange(results);

                sourceStatuses.Add(new SourceExecutionStatus
                {
                    AgentType = agentType,
                    Succeeded = true,
                    ResultsCount = results.Length,
                    Duration = elapsed,
                    ErrorMessage = null
                });

                // Early exit if we have enough results and this is a high-confidence source
                if (aggregatedResults.Count >= searchParameters.MaxResults && agentType == SearchAgentType.VectorSearch)
                {
                    _logger.LogInformation("Sufficient results from primary index search, skipping fallback sources");
                    return aggregatedResults.ToArray();
                }
            }
            else
            {
                executionMetrics[agentType] = (TimeSpan.Zero, 0);
                sourceStatuses.Add(new SourceExecutionStatus
                {
                    AgentType = agentType,
                    Succeeded = false,
                    ResultsCount = 0,
                    Duration = elapsed,
                    ErrorMessage = "Search failed or agent not found"
                });
            }
        }

        stopwatch.Stop();

        var degradedMode = sourceStatuses.Any(s => !s.Succeeded);
        var failedSources = sourceStatuses.Where(s => !s.Succeeded).ToList();
        var successfulSources = sourceStatuses.Where(s => s.Succeeded).ToList();

        if (degradedMode)
        {
            _degradedModeTracker.TrackSearchExecution(
                failedSources.Concat(successfulSources).ToList(),
                stopwatch.Elapsed,
                aggregatedResults.Count);
        }

        if (context.QueryContext != null)
        {
            UpdateQueryContextMetrics(context, executionMetrics, degradedMode, failedSources, successfulSources);
        }

        return await _resultFusionService.FuseAndRankResultsAsync(aggregatedResults, query, searchParameters, degradedMode);
    }

    private static void UpdateQueryContextMetrics(
        SearchContext context,
        Dictionary<SearchAgentType, (TimeSpan Duration, int ResultsFound)> executionMetrics,
        bool degradedMode,
        List<SourceExecutionStatus> failedSources,
        List<SourceExecutionStatus> successfulSources)
    {
        if (context.QueryContext == null) return;

        context.QueryContext.AdditionalProperties["SearchPatternMetrics"] = new SearchPatternMetrics
        {
            VectorSearchExecuted = executionMetrics.ContainsKey(SearchAgentType.VectorSearch),
            WebSearchExecuted = executionMetrics.ContainsKey(SearchAgentType.WebSearch),
            PDFSearchExecuted = executionMetrics.ContainsKey(SearchAgentType.PDFSearch),
            VectorSearchTime = executionMetrics.TryGetValue(SearchAgentType.VectorSearch, out var v) ? v.Duration : TimeSpan.Zero,
            WebSearchTime = executionMetrics.TryGetValue(SearchAgentType.WebSearch, out var w) ? w.Duration : TimeSpan.Zero,
            PDFSearchTime = executionMetrics.TryGetValue(SearchAgentType.PDFSearch, out var p) ? p.Duration : TimeSpan.Zero,
            VectorResultsFound = executionMetrics.TryGetValue(SearchAgentType.VectorSearch, out var vr) ? vr.ResultsFound : 0,
            WebResultsFound = executionMetrics.TryGetValue(SearchAgentType.WebSearch, out var wr) ? wr.ResultsFound : 0,
            PDFResultsFound = executionMetrics.TryGetValue(SearchAgentType.PDFSearch, out var pr) ? pr.ResultsFound : 0
        };

        context.QueryContext.AdditionalProperties["DegradedMode"] = degradedMode;
        if (degradedMode)
        {
            context.QueryContext.AdditionalProperties["FailedSources"] = failedSources.Select(s => new { s.AgentType, s.ErrorMessage }).ToList();
            context.QueryContext.AdditionalProperties["AvailableSources"] = successfulSources.Select(s => new { s.AgentType, s.ResultsCount }).ToList();
        }
    }

    #endregion

    #region Helpers

    private async Task<(SearchResult[]? Results, TimeSpan Elapsed)> ExecuteAgentSearchWithMetricsAsync(
        SearchAgentType agentType,
        string query,
        SearchParameters searchParameters)
    {
        var agent = _agents.FirstOrDefault(a => a.AgentType == agentType);
        if (agent == null)
        {
            return (null, TimeSpan.Zero);
        }

        var agentStopwatch = Stopwatch.StartNew();
        try
        {
            var results = await agent.SearchAsync(query, searchParameters);
            agentStopwatch.Stop();
            return (results, agentStopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            agentStopwatch.Stop();
            _logger.LogWarning(ex, "Agent {AgentType} failed – continuing with remaining sources", agentType);
            return (null, agentStopwatch.Elapsed);
        }
    }

    private static SearchParameters BuildSearchOptions(SearchContext context)
    {
        var prefs = context.Preferences ?? new SearchPreferences();
        return new SearchParameters
        {
            MaxResults = prefs.MaxResults,
            MinRelevanceScore = prefs.MinRelevanceScore,
            EnableCaching = true,
            IncludeMetadata = true
        };
    }

    internal static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length <= maxLength)
            return text;

        return text[..maxLength] + "…";
    }

    #endregion
}

