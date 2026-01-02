using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Application.Agents;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.DTOs;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Domain.Entities;

namespace MotorcycleRAG.Application.Services;

/// <summary>
/// Coordinates multiple search agents using the Microsoft Agent Framework to rank and fuse their results.
/// Integrates MCP (Model Context Protocol) tool configuration for extensible tool management.
/// </summary>
public sealed class AgentOrchestrator : IAgentOrchestrator
{
    private readonly IReadOnlyList<ISearchAgent> _agents;
    private readonly IAzureOpenAIClient _openAIClient;
    private readonly SearchOptions _searchConfig;
    private readonly ILogger<AgentOrchestrator> _logger;
    private readonly AgentFrameworkAdapter _frameworkAdapter;
    private readonly AgentState _executionState;
    private readonly IMcpConfigurationProvider _mcpConfigProvider;
    private McpToolConfiguration[]? _cachedEnabledTools;
    private DateTime _lastToolRefresh = DateTime.MinValue;
    private readonly TimeSpan _toolRefreshInterval = TimeSpan.FromMinutes(5);

    public AgentOrchestrator(
        IEnumerable<ISearchAgent> agents,
        IAzureOpenAIClient openAIClient,
        IOptions<SearchOptions> searchConfig,
        ILogger<AgentOrchestrator> logger,
        IMcpConfigurationProvider mcpConfigProvider)
    {
        _agents = agents?.ToList() ?? throw new ArgumentNullException(nameof(agents));
        _openAIClient = openAIClient ?? throw new ArgumentNullException(nameof(openAIClient));
        _searchConfig = searchConfig?.Value ?? throw new ArgumentNullException(nameof(searchConfig));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mcpConfigProvider = mcpConfigProvider ?? throw new ArgumentNullException(nameof(mcpConfigProvider));

        _frameworkAdapter = new AgentFrameworkAdapter(logger);
        _executionState = new AgentState();

        InitializeFrameworkAdapter();
        InitializeMcpTools();
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
                _ => $"agent_{agent.AgentType.ToString().ToLower()}"
            };

            var handler = AgentFrameworkAdapter.CreateSearchAgentHandler(agent, _logger);
            _frameworkAdapter.RegisterToolHandler(toolName, handler);
        }

        _logger.LogInformation("Agent Framework adapter initialized with {AgentCount} agents", _agents.Count);
    }

    /// <summary>
    /// Initialize MCP tool configurations
    /// </summary>
    private void InitializeMcpTools()
    {
        try
        {
            // Load enabled tools on startup
            _cacheMcpToolsAsync().GetAwaiter().GetResult();
            _logger.LogInformation("MCP tools initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize MCP tools on startup - will retry later");
        }
    }

    /// <summary>
    /// Cache enabled MCP tools with refresh interval
    /// </summary>
    private async Task _cacheMcpToolsAsync()
    {
        var now = DateTime.UtcNow;
        if (_lastToolRefresh != DateTime.MinValue &&
            (now - _lastToolRefresh) < _toolRefreshInterval)
        {
            // Use cached tools if refresh interval hasn't elapsed
            return;
        }

        try
        {
            _cachedEnabledTools = await _mcpConfigProvider.GetEnabledToolsAsync();
            _lastToolRefresh = now;
            _logger.LogDebug("Cached {ToolCount} enabled MCP tools", _cachedEnabledTools.Length);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load MCP tools - orchestration will continue with builtin agents only");
            _cachedEnabledTools ??= Array.Empty<McpToolConfiguration>();
        }
    }

    /// <summary>
    /// Get enabled MCP tools for current execution
    /// </summary>
    private async Task<McpToolConfiguration[]> GetEnabledMcpToolsAsync()
    {
        await _cacheMcpToolsAsync();
        return _cachedEnabledTools ?? Array.Empty<McpToolConfiguration>();
    }

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
                _logger.LogInformation("Executing search with {McpToolCount} enabled MCP tools: {Tools}",
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
            throw;
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
You are an expert on motorcycle maintenance and specification.  
Using only the information provided in the snippets below, answer the user's question.  
Cite the snippet identifier (e.g. "[1]") after every statement that comes from a snippet.  
If the answer cannot be determined from the snippets, say you do not have sufficient information.  

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate response via OpenAI");
            _executionState.RecordError("ResponseGenerator", ex.Message, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<SearchResult[]> OrchestrateSearchAsync(string query, SearchParameters options)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            _logger.LogWarning("OrchestrateSearchAsync was invoked with an empty query");
            return Array.Empty<SearchResult>();
        }

        var context = new SearchContext
        {
            Preferences = new SearchPreferences
            {
                MaxResults = options.MaxResults,
                MinRelevanceScore = options.MinRelevanceScore
            }
        };

        return await ExecuteSequentialSearchAsync(query, context);
    }

    /// <inheritdoc />
    public IEnumerable<ISearchAgent> GetAvailableAgents()
    {
        return _agents;
    }

    #endregion

    #region Parallel Execution Helpers

    /// <summary>
    /// Executes all agents in parallel and returns the merged & ranked results.
    /// This is not part of the public interface yet but can be exposed later.
    /// </summary>
    private async Task<SearchResult[]> ExecuteParallelSearchInternalAsync(string query, SearchContext context)
    {
        var searchParameters = BuildSearchOptions(context);
        var searchTasks = _agents.Select(async agent =>
        {
            try
            {
                _logger.LogInformation("Running {AgentType} agent in parallel…", agent.AgentType);
                return await agent.SearchAsync(query, searchParameters);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Parallel execution – agent {AgentType} failed", agent.AgentType);
                return Array.Empty<SearchResult>();
            }
        }).ToArray();

        var results = await Task.WhenAll(searchTasks);
        var aggregated = results.SelectMany(r => r).ToList();
        return await FuseAndRankResultsAsync(aggregated, query, searchParameters);
    }

    /// <summary>
    /// Executes sequential retrieval policy: index → web → pdf fallback
    /// </summary>
    private async Task<SearchResult[]> ExecuteSequentialRetrievalPolicyAsync(string query, SearchContext context)
    {
        var searchParameters = BuildSearchOptions(context);
        var aggregatedResults = new List<SearchResult>();
        var executionMetrics = new Dictionary<SearchAgentType, (TimeSpan Duration, int ResultsFound)>();

        // Define the execution order: VectorSearch (index) → WebSearch → PDFSearch (fallback)
        var executionOrder = new[]
        {
            SearchAgentType.VectorSearch,
            SearchAgentType.WebSearch,
            SearchAgentType.PDFSearch
        };

        var stopwatch = Stopwatch.StartNew();

        foreach (var agentType in executionOrder)
        {
            var agent = _agents.FirstOrDefault(a => a.AgentType == agentType);
            if (agent == null)
            {
                _logger.LogDebug("No agent found for {AgentType}, skipping", agentType);
                continue;
            }

            try
            {
                _logger.LogInformation("Executing {AgentType} in sequential retrieval policy…", agentType);
                var agentStopwatch = Stopwatch.StartNew();

                var results = await agent.SearchAsync(query, searchParameters);
                agentStopwatch.Stop();

                executionMetrics[agentType] = (agentStopwatch.Elapsed, results.Length);
                aggregatedResults.AddRange(results);

                _logger.LogInformation("Agent {AgentType} completed: {Results} results in {Duration}ms",
                    agentType, results.Length, agentStopwatch.ElapsedMilliseconds);

                // Early exit if we have enough results and this is a high-confidence source
                if (aggregatedResults.Count >= searchParameters.MaxResults && agentType == SearchAgentType.VectorSearch)
                {
                    _logger.LogInformation("Sufficient results from primary index search, skipping fallback sources");
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Agent {AgentType} failed in sequential policy – continuing with fallback", agentType);
                executionMetrics[agentType] = (TimeSpan.Zero, 0);
            }
        }

        stopwatch.Stop();
        _logger.LogInformation("Sequential retrieval policy completed in {Duration}ms with {TotalResults} total results",
            stopwatch.ElapsedMilliseconds, aggregatedResults.Count);

        // Update search pattern metrics
        if (context.QueryContext != null)
        {
            context.QueryContext.AdditionalProperties["SearchPatternMetrics"] = new SearchPatternMetrics
            {
                VectorSearchExecuted = executionMetrics.ContainsKey(SearchAgentType.VectorSearch),
                WebSearchExecuted = executionMetrics.ContainsKey(SearchAgentType.WebSearch),
                PDFSearchExecuted = executionMetrics.ContainsKey(SearchAgentType.PDFSearch),
                VectorSearchTime = executionMetrics.TryGetValue(SearchAgentType.VectorSearch, out var vectorMetrics) ? vectorMetrics.Duration : TimeSpan.Zero,
                WebSearchTime = executionMetrics.TryGetValue(SearchAgentType.WebSearch, out var webMetrics) ? webMetrics.Duration : TimeSpan.Zero,
                PDFSearchTime = executionMetrics.TryGetValue(SearchAgentType.PDFSearch, out var pdfMetrics) ? pdfMetrics.Duration : TimeSpan.Zero,
                VectorResultsFound = executionMetrics.TryGetValue(SearchAgentType.VectorSearch, out var vectorResults) ? vectorResults.ResultsFound : 0,
                WebResultsFound = executionMetrics.TryGetValue(SearchAgentType.WebSearch, out var webResults) ? webResults.ResultsFound : 0,
                PDFResultsFound = executionMetrics.TryGetValue(SearchAgentType.PDFSearch, out var pdfResults) ? pdfResults.ResultsFound : 0
            };
        }

        return await FuseAndRankResultsAsync(aggregatedResults, query, searchParameters);
    }

    #endregion

    #region Result Fusion & Ranking

    private async Task<SearchResult[]> FuseAndRankResultsAsync(List<SearchResult> results, string query, SearchParameters options)
    {
        if (results.Count == 0)
            return Array.Empty<SearchResult>();

        // Remove duplicates (same Source.DocumentId or Id if available).
        var deduped = results.GroupBy(r => string.IsNullOrWhiteSpace(r.Source.DocumentId) ? r.Id : r.Source.DocumentId)
                              .Select(g => g.OrderByDescending(r => r.RelevanceScore).First())
                              .ToList();

        // Optionally apply semantic ranking.
        if (_searchConfig.EnableSemanticRanking)
        {
            try
            {
                deduped = await ApplySemanticRankingAsync(query, deduped);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Semantic ranking failed – falling back to relevance score only");
                deduped = deduped.OrderByDescending(r => r.RelevanceScore).ToList();
            }
        }
        else
        {
            deduped = deduped.OrderByDescending(r => r.RelevanceScore).ToList();
        }

        return deduped.Take(options.MaxResults).ToArray();
    }

    private async Task<List<SearchResult>> ApplySemanticRankingAsync(string query, List<SearchResult> results)
    {
        // Generate embedding for the query.
        var queryEmbedding = await _openAIClient.GetEmbeddingAsync("text-embedding-3-large", query, CancellationToken.None);

        // Generate embeddings for each candidate result (truncate content to keep costs low).
        var contents = results.Select(r => Truncate(r.Content, 1024)).ToArray();
        var resultEmbeddings = await _openAIClient.GetEmbeddingsAsync("text-embedding-3-large", contents, CancellationToken.None);

        var scored = new List<(SearchResult Result, double Score)>();
        for (var i = 0; i < results.Count; i++)
        {
            var semanticScore = CosineSimilarity(queryEmbedding, resultEmbeddings[i]);
            // Blend the agent-provided relevance score with the semantic similarity.
            var blendedScore = results[i].RelevanceScore * 0.7 + (float)semanticScore * 0.3f;
            scored.Add((results[i], blendedScore));
        }

        return scored.OrderByDescending(s => s.Score).Select(s => s.Result).ToList();
    }

    private static double CosineSimilarity(float[] v1, float[] v2)
    {
        if (v1.Length != v2.Length)
            return 0;

        double dot = 0, mag1 = 0, mag2 = 0;
        for (int i = 0; i < v1.Length; i++)
        {
            dot += v1[i] * v2[i];
            mag1 += Math.Pow(v1[i], 2);
            mag2 += Math.Pow(v2[i], 2);
        }

        return dot / (Math.Sqrt(mag1) * Math.Sqrt(mag2) + 1e-8);
    }

    #endregion

    #region Helpers

    private static SearchParameters BuildSearchOptions(SearchContext context)
    {
        var prefs = context.Preferences ?? new SearchPreferences();
        return new SearchParameters {
            MaxResults = prefs.MaxResults,
            MinRelevanceScore = prefs.MinRelevanceScore,
            EnableCaching = true,
            IncludeMetadata = true
        };
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length <= maxLength)
            return text;
        return text[..maxLength] + "…";
    }

    #endregion
}
