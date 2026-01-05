using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Application.Agents;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Domain.Entities;

namespace MotorcycleRAG.Application.Services;

/// <summary>
/// Coordinates multiple search agents using the Microsoft Agent Framework to rank and fuse their results.
/// Integrates MCP (Model Context Protocol) tool configuration for extensible tool management.
/// Implements partial-results aggregation for graceful degradation when sources become unavailable.
/// </summary>
public sealed class AgentOrchestrator : IAgentOrchestrator {
    private readonly IReadOnlyList<ISearchAgent> _agents;
    private readonly IAzureOpenAIClient _openAIClient;
    private readonly SearchOptions _searchConfig;
    private readonly ILogger<AgentOrchestrator> _logger;
    private readonly AgentFrameworkAdapter _frameworkAdapter;
    private readonly AgentState _executionState;
    private readonly IMcpConfigurationProvider _mcpConfigProvider;
    private readonly ICorrelationService _correlationService;
    private readonly ITelemetryService _telemetryService;
    private McpToolConfiguration[]? _cachedEnabledTools;
    private DateTime _lastToolRefresh = DateTime.MinValue;
    private readonly TimeSpan _toolRefreshInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Tracks which sources have succeeded or failed during orchestration
    /// </summary>
    private class SourceExecutionStatus {
        public SearchAgentType AgentType { get; set; }
        public bool Succeeded { get; set; }
        public int ResultsCount { get; set; }
        public TimeSpan Duration { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public AgentOrchestrator(
        IEnumerable<ISearchAgent> agents,
        IAzureOpenAIClient openAIClient,
        IOptions<SearchOptions> searchConfig,
        ILogger<AgentOrchestrator> logger,
        IMcpConfigurationProvider mcpConfigProvider,
        ICorrelationService correlationService,
        ITelemetryService telemetryService) {
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(openAIClient);
        ArgumentNullException.ThrowIfNull(searchConfig);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(mcpConfigProvider);
        ArgumentNullException.ThrowIfNull(correlationService);
        ArgumentNullException.ThrowIfNull(telemetryService);

        _agents = agents.ToList();
        _openAIClient = openAIClient;
        _searchConfig = searchConfig.Value;
        _logger = logger;
        _mcpConfigProvider = mcpConfigProvider;
        _correlationService = correlationService;
        _telemetryService = telemetryService;

        _frameworkAdapter = new AgentFrameworkAdapter(logger);
        _executionState = new AgentState();

        InitializeFrameworkAdapter();
        InitializeMcpTools();
    }

    /// <summary>
    /// Initialize the Agent Framework adapter with registered agents
    /// </summary>
    private void InitializeFrameworkAdapter() {
        foreach (var agent in _agents) {
            var toolName = agent.AgentType switch {
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
    private void InitializeMcpTools() {
        try {
            // Load enabled tools on startup
            CacheMcpToolsAsync().GetAwaiter().GetResult();
            _logger.LogInformation("MCP tools initialized successfully");
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "Failed to initialize MCP tools on startup - will retry later");
        }
    }

    /// <summary>
    /// Cache enabled MCP tools with refresh interval
    /// </summary>
    private async Task CacheMcpToolsAsync() {
        var now = DateTime.UtcNow;
        if (_lastToolRefresh != DateTime.MinValue && (now - _lastToolRefresh) < _toolRefreshInterval) {
            // Use cached tools if refresh interval hasn't elapsed
            return;
        }

        try {
            _cachedEnabledTools = await _mcpConfigProvider.GetEnabledToolsAsync();
            _lastToolRefresh = now;
            _logger.LogDebug("Cached {ToolCount} enabled MCP tools", _cachedEnabledTools.Length);
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "Failed to load MCP tools - orchestration will continue with builtin agents only");
            _cachedEnabledTools ??= Array.Empty<McpToolConfiguration>();
        }
    }

    /// <summary>
    /// Get enabled MCP tools for current execution
    /// </summary>
    private async Task<McpToolConfiguration[]> GetEnabledMcpToolsAsync() {
        await CacheMcpToolsAsync();
        return _cachedEnabledTools ?? Array.Empty<McpToolConfiguration>();
    }

    #region IAgentOrchestrator Implementation

    /// <inheritdoc />
    public async Task<SearchResult[]> ExecuteSequentialSearchAsync(string query, SearchContext context) {
        if (string.IsNullOrWhiteSpace(query)) {
            _logger.LogWarning("ExecuteSequentialSearchAsync was invoked with an empty query");
            return Array.Empty<SearchResult>();
        }

        context ??= new SearchContext();

        _executionState.OriginalQuery = query;
        _executionState.SearchContext = context;
        _executionState.Status = AgentExecutionStatus.Running;

        try {
            // Load enabled MCP tools for this execution
            var enabledMcpTools = await GetEnabledMcpToolsAsync();
            if (enabledMcpTools.Length > 0) {
                _logger.LogInformation("Executing search with {McpToolCount} enabled MCP tools: {Tools}",
                    enabledMcpTools.Length,
                    string.Join(", ", enabledMcpTools.Select(t => t.ToolId)));
            }

            // Use the sequential retrieval policy: index → web → pdf fallback
            var results = await ExecuteSequentialRetrievalPolicyAsync(query, context);

            _executionState.MarkComplete();
            return results;
        }
        catch (Exception ex) {
            _executionState.MarkFailed();
            _logger.LogError(ex, "Sequential search execution failed");
            throw new InvalidOperationException("Sequential search execution failed", ex);
        }
    }

    /// <inheritdoc />
    public async Task<string> GenerateResponseAsync(SearchResult[] results, string originalQuery) {
        if (results == null || results.Length == 0) {
            _logger.LogWarning("GenerateResponseAsync called with no results – returning empty response.");
            return string.Empty;
        }

        try {
            _logger.LogInformation("Generating response for query: {Query}", originalQuery);

            var snippets = results.Take(10)
                                  .Select(r => $"[{r.Id}] {Truncate(r.Content, 500)}")
                                  .ToArray();

            var prompt = $"""
User question: "{originalQuery}"

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
        catch (Exception ex) {
            _logger.LogError(ex, "Failed to generate response via OpenAI");
            _executionState.RecordError("ResponseGenerator", ex.Message, ex);
            throw new InvalidOperationException("Failed to generate response via OpenAI", ex);
        }
    }

    /// <inheritdoc />
    public async Task<SearchResult[]> OrchestrateSearchAsync(string query, SearchParameters options) {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(query)) {
            _logger.LogWarning("OrchestrateSearchAsync was invoked with an empty query");
            return Array.Empty<SearchResult>();
        }

        var context = new SearchContext {
            Preferences = new SearchPreferences {
                MaxResults = options.MaxResults,
                MinRelevanceScore = options.MinRelevanceScore
            }
        };

        return await ExecuteSequentialSearchAsync(query, context);
    }

    /// <inheritdoc />
    public IEnumerable<ISearchAgent> GetAvailableAgents() {
        return _agents;
    }

    #endregion

    #region Sequential Execution Logic

    /// <summary>
    /// Executes sequential retrieval policy: index → web → pdf fallback.
    /// Implements partial-results aggregation with degraded-mode tracking.
    /// When sources fail, continues with remaining available sources and tracks the degradation.
    /// </summary>
    private async Task<SearchResult[]> ExecuteSequentialRetrievalPolicyAsync(string query, SearchContext context) {
        var searchParameters = BuildSearchOptions(context);
        var aggregatedResults = new List<SearchResult>();
        var sourceStatuses = new List<SourceExecutionStatus>();
        var executionMetrics = new Dictionary<SearchAgentType, (TimeSpan Duration, int ResultsFound)>();

        // Define the execution order: VectorSearch (index) → WebSearch → PDFSearch (fallback)
        var executionOrder = new[]
        {
            SearchAgentType.VectorSearch,
            SearchAgentType.WebSearch,
            SearchAgentType.PDFSearch
        };

        var stopwatch = Stopwatch.StartNew();

        foreach (var agentType in executionOrder) {
            var (results, elapsed) = await ExecuteAgentSearchWithMetricsAsync(agentType, query, searchParameters);
            
            if (results != null) {
                executionMetrics[agentType] = (elapsed, results.Length);
                aggregatedResults.AddRange(results);

                sourceStatuses.Add(new SourceExecutionStatus {
                    AgentType = agentType,
                    Succeeded = true,
                    ResultsCount = results.Length,
                    Duration = elapsed,
                    ErrorMessage = null
                });

                // Early exit if we have enough results and this is a high-confidence source
                if (aggregatedResults.Count >= searchParameters.MaxResults && agentType == SearchAgentType.VectorSearch) {
                    _logger.LogInformation("Sufficient results from primary index search, skipping fallback sources");
                    return aggregatedResults.ToArray();
                }
            }
            else {
                executionMetrics[agentType] = (TimeSpan.Zero, 0);
                sourceStatuses.Add(new SourceExecutionStatus {
                    AgentType = agentType,
                    Succeeded = false,
                    ResultsCount = 0,
                    Duration = elapsed,
                    ErrorMessage = "Search failed or agent not found"
                });
            }
        }

        stopwatch.Stop();

        // Determine if system is operating in degraded mode
        var degradedMode = sourceStatuses.Any(s => !s.Succeeded);
        var failedSources = sourceStatuses.Where(s => !s.Succeeded).ToList();
        var successfulSources = sourceStatuses.Where(s => s.Succeeded).ToList();
        var correlationId = _correlationService.GetOrCreateCorrelationId();

        if (degradedMode) {
            TrackDegradedMode(correlationId, failedSources, successfulSources, stopwatch.Elapsed, aggregatedResults.Count);
        }

        // Update search pattern metrics
        if (context?.QueryContext != null) {
            UpdateQueryContextMetrics(context, executionMetrics, degradedMode, failedSources, successfulSources);
        }

        return await FuseAndRankResultsAsync(aggregatedResults, query, searchParameters, degradedMode);
    }

    private void TrackDegradedMode(string correlationId, List<SourceExecutionStatus> failedSources, List<SourceExecutionStatus> successfulSources, TimeSpan totalDuration, int totalResults) {
        var failedSourceList = failedSources.Select(s => s.AgentType.ToString()).ToList();
        var availableSourceList = successfulSources.Select(s => s.AgentType.ToString()).ToList();

        _telemetryService.TrackDegradedMode(correlationId, failedSourceList, availableSourceList, totalDuration, totalResults);

        foreach (var failedSource in failedSources) {
            _telemetryService.TrackSourceFailure(
                correlationId,
                failedSource.AgentType.ToString(),
                failedSource.ErrorMessage ?? "Unknown error",
                failedSource.Duration);
        }
    }

    private static void UpdateQueryContextMetrics(
        SearchContext context,
        Dictionary<SearchAgentType, (TimeSpan Duration, int ResultsFound)> executionMetrics,
        bool degradedMode,
        List<SourceExecutionStatus> failedSources,
        List<SourceExecutionStatus> successfulSources) {
        if (context.QueryContext == null) return;

        context.QueryContext.AdditionalProperties["SearchPatternMetrics"] = new SearchPatternMetrics {
            VectorSearchExecuted = executionMetrics.ContainsKey(SearchAgentType.VectorSearch),
            WebSearchExecuted = executionMetrics.ContainsKey(SearchAgentType.WebSearch),
            PDFSearchExecuted = executionMetrics.ContainsKey(SearchAgentType.PDFSearch),
            VectorSearchTime = executionMetrics.TryGetValue(SearchAgentType.VectorSearch, out var v)
                ? v.Duration : TimeSpan.Zero,
            WebSearchTime = executionMetrics.TryGetValue(SearchAgentType.WebSearch, out var w)
                ? w.Duration : TimeSpan.Zero,
            PDFSearchTime = executionMetrics.TryGetValue(SearchAgentType.PDFSearch, out var p)
                ? p.Duration : TimeSpan.Zero,
            VectorResultsFound = executionMetrics.TryGetValue(SearchAgentType.VectorSearch, out var vr)
                ? vr.ResultsFound : 0,
            WebResultsFound = executionMetrics.TryGetValue(SearchAgentType.WebSearch, out var wr)
                ? wr.ResultsFound : 0,
            PDFResultsFound = executionMetrics.TryGetValue(SearchAgentType.PDFSearch, out var pr)
                ? pr.ResultsFound : 0
        };

        context.QueryContext.AdditionalProperties["DegradedMode"] = degradedMode;
        if (degradedMode) {
            context.QueryContext.AdditionalProperties["FailedSources"] = failedSources.Select(s => new { s.AgentType, s.ErrorMessage }).ToList();
            context.QueryContext.AdditionalProperties["AvailableSources"] = successfulSources.Select(s => new { s.AgentType, s.ResultsCount }).ToList();
        }
    }

    #endregion

    #region Result Fusion & Ranking

    /// <summary>
    /// Fuses and ranks search results from multiple agents.
    /// When degraded mode is active, adds metadata indicating which sources were unavailable.
    /// </summary>
    private async Task<SearchResult[]> FuseAndRankResultsAsync(List<SearchResult> results, string query, SearchParameters options, bool degradedMode) {
        if (results.Count == 0) {
            return Array.Empty<SearchResult>();
        }

        // Remove duplicates.
        var deduped = results.GroupBy(r => string.IsNullOrWhiteSpace(r.Source.DocumentId) ? r.Id : r.Source.DocumentId)
                              .Select(g => g.OrderByDescending(r => r.RelevanceScore).First())
                              .ToList();

        // Optionally apply semantic ranking.
        if (_searchConfig.EnableSemanticRanking) {
            try {
                deduped = await ApplySemanticRankingAsync(query, deduped);
            }
            catch (Exception ex) {
                _logger.LogWarning(ex, "Semantic ranking failed – falling back to relevance score only");
                deduped = deduped.OrderByDescending(r => r.RelevanceScore).ToList();
            }
        }
        else {
            deduped = deduped.OrderByDescending(r => r.RelevanceScore).ToList();
        }

        var finalResults = deduped.Take(options.MaxResults).ToArray();

        // Add degraded mode metadata to results
        if (degradedMode && finalResults.Length > 0) {
            foreach (var metadata in finalResults.Select(r => r.Metadata)) {
                metadata["DegradedMode"] = true;
                metadata["Note"] = "Results from partial sources due to unavailable service(s).";
            }
        }

        return finalResults;
    }

    private async Task<List<SearchResult>> ApplySemanticRankingAsync(string query, List<SearchResult> results) {
        // Generate embedding for the query.
        var queryEmbedding = await _openAIClient.GetEmbeddingAsync("text-embedding-3-large", query, CancellationToken.None);

        // Generate embeddings for each candidate result.
        var contents = results.Select(r => Truncate(r.Content, 1024)).ToArray();
        var resultEmbeddings = await _openAIClient.GetEmbeddingsAsync("text-embedding-3-large", contents, CancellationToken.None);

        var scored = new List<(SearchResult Result, double Score)>();
        for (var i = 0; i < results.Count; i++) {
            var semanticScore = CosineSimilarity(queryEmbedding, resultEmbeddings[i]);
            var blendedScore = (results[i].RelevanceScore * 0.7) + (semanticScore * 0.3);
            scored.Add((results[i], blendedScore));
        }

        return scored.OrderByDescending(s => s.Score).Select(s => s.Result).ToList();
    }

    private static double CosineSimilarity(float[] v1, float[] v2) {
        if (v1.Length != v2.Length)
            return 0;

        double dot = 0;
        double mag1 = 0;
        double mag2 = 0;
        for (int i = 0; i < v1.Length; i++) {
            dot += v1[i] * v2[i];
            mag1 += Math.Pow(v1[i], 2);
            mag2 += Math.Pow(v2[i], 2);
        }

        return dot / (Math.Sqrt(mag1) * Math.Sqrt(mag2) + 1e-8);
    }

    #endregion

    #region Helpers

    private async Task<(SearchResult[]? Results, TimeSpan Elapsed)> ExecuteAgentSearchWithMetricsAsync(SearchAgentType agentType, string query, SearchParameters searchParameters) {
        var agent = _agents.FirstOrDefault(a => a.AgentType == agentType);
        if (agent == null) {
            return (null, TimeSpan.Zero);
        }

        var agentStopwatch = Stopwatch.StartNew();
        try {
            var results = await agent.SearchAsync(query, searchParameters);
            agentStopwatch.Stop();
            return (results, agentStopwatch.Elapsed);
        }
        catch (Exception ex) {
            agentStopwatch.Stop();
            _logger.LogWarning(ex, "Agent {AgentType} failed – continuing with remaining sources", agentType);
            return (null, agentStopwatch.Elapsed);
        }
    }

    private static SearchParameters BuildSearchOptions(SearchContext context) {
        var prefs = context.Preferences ?? new SearchPreferences();
        return new SearchParameters {
            MaxResults = prefs.MaxResults,
            MinRelevanceScore = prefs.MinRelevanceScore,
            EnableCaching = true,
            IncludeMetadata = true
        };
    }

    private static string Truncate(string text, int maxLength) {
        if (string.IsNullOrWhiteSpace(text) || text.Length <= maxLength)
            return text;
        return text[..maxLength] + "…";
    }

    #endregion
}
