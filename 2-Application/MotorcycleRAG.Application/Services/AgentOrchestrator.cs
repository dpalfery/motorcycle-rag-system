using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Application.Agents;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Models;

namespace MotorcycleRAG.Application.Services;

/// <summary>
/// Coordinates multiple search agents using the Microsoft Agent Framework to rank and fuse their results.
/// </summary>
public sealed class AgentOrchestrator : IAgentOrchestrator
{
    private readonly IReadOnlyList<ISearchAgent> _agents;
    private readonly IAzureOpenAIClient _openAIClient;
    private readonly SearchConfiguration _searchConfig;
    private readonly ILogger<AgentOrchestrator> _logger;
    private readonly AgentFrameworkAdapter _frameworkAdapter;
    private readonly AgentState _executionState;

    public AgentOrchestrator(
        IEnumerable<ISearchAgent> agents,
        IAzureOpenAIClient openAIClient,
        IOptions<SearchConfiguration> searchConfig,
        ILogger<AgentOrchestrator> logger)
    {
        _agents = agents?.ToList() ?? throw new ArgumentNullException(nameof(agents));
        _openAIClient = openAIClient ?? throw new ArgumentNullException(nameof(openAIClient));
        _searchConfig = searchConfig?.Value ?? throw new ArgumentNullException(nameof(searchConfig));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _frameworkAdapter = new AgentFrameworkAdapter(logger);
        _executionState = new AgentState();

        InitializeFrameworkAdapter();
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
        var searchOptions = BuildSearchOptions(context);
        var aggregatedResults = new List<SearchResult>();

        _executionState.OriginalQuery = query;
        _executionState.SearchContext = context;
        _executionState.SearchOptions = searchOptions;
        _executionState.Status = AgentExecutionStatus.Running;

        try
        {
            foreach (var agent in _agents)
            {
                try
                {
                    _logger.LogInformation("Running {AgentType} agent sequentially…", agent.AgentType);
                    _executionState.AddMessage(
                        agent.AgentType.ToString(),
                        $"Executing search for: {query}",
                        AgentMessageType.SearchQuery);

                    var results = await agent.SearchAsync(query, searchOptions);
                    aggregatedResults.AddRange(results);

                    _executionState.AccumulatedResults.AddRange(results);
                    _executionState.AddMessage(
                        agent.AgentType.ToString(),
                        $"Found {results.Length} results",
                        AgentMessageType.SearchResult);

                    if (aggregatedResults.Count >= searchOptions.MaxResults)
                    {
                        _logger.LogInformation("Desired number of results collected – skipping remaining agents.");
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Agent {AgentType} failed – continuing with remaining agents", agent.AgentType);
                    _executionState.RecordError(agent.AgentType.ToString(), ex.Message, ex);
                }
            }

            var fused = await FuseAndRankResultsAsync(aggregatedResults, query, searchOptions);
            _executionState.MarkComplete();
            return fused;
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
    public async Task<SearchResult[]> OrchestrateSearchAsync(string query, SearchOptions options)
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
        var searchOptions = BuildSearchOptions(context);
        var searchTasks = _agents.Select(async agent =>
        {
            try
            {
                _logger.LogInformation("Running {AgentType} agent in parallel…", agent.AgentType);
                return await agent.SearchAsync(query, searchOptions);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Parallel execution – agent {AgentType} failed", agent.AgentType);
                return Array.Empty<SearchResult>();
            }
        }).ToArray();

        var results = await Task.WhenAll(searchTasks);
        var aggregated = results.SelectMany(r => r).ToList();
        return await FuseAndRankResultsAsync(aggregated, query, searchOptions);
    }

    #endregion

    #region Result Fusion & Ranking

    private async Task<SearchResult[]> FuseAndRankResultsAsync(List<SearchResult> results, string query, SearchOptions options)
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

    private static SearchOptions BuildSearchOptions(SearchContext context)
    {
        var prefs = context.Preferences ?? new SearchPreferences();
        return new SearchOptions
        {
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
