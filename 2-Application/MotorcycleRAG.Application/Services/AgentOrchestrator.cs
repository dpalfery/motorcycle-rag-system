using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using MotorcycleRAG.Core.Interfaces;
using MotorcycleRAG.Core.Models;

namespace MotorcycleRAG.Core.Services;

/// <summary>
/// Coordinates multiple search agents using Semantic Kernel to rank and fuse their results.
/// </summary>
public sealed class AgentOrchestrator : IAgentOrchestrator
{
    private readonly IReadOnlyList<ISearchAgent> _agents;
    private readonly IAzureOpenAIClient _openAIClient;
    private readonly SearchConfiguration _searchConfig;
    private readonly ILogger<AgentOrchestrator> _logger;
    private readonly Kernel _kernel;

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

        // Build a lightweight Semantic Kernel instance.  
        // We do not configure specific AI connectors here – callers using the Kernel can add those through plugins.  
        // This keeps the orchestrator free from configuration secrets and makes it easier to test.
        _kernel = Kernel.CreateBuilder().Build();
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

        // Execute agents one-by-one – this allows earlier agents to short-circuit if enough high-quality
        // answers are already found, saving costs.
        foreach (var agent in _agents)
        {
            try
            {
                _logger.LogInformation("Running {AgentType} agent sequentially…", agent.AgentType);
                var results = await agent.SearchAsync(query, searchOptions);
                aggregatedResults.AddRange(results);

                // If we already have the maximum desired results we can stop early.
                if (aggregatedResults.Count >= searchOptions.MaxResults)
                {
                    _logger.LogInformation("Desired number of results collected – skipping remaining agents.");
                    break;
                }
            }
            catch (Exception ex)
            {
                // We do not want one agent failure to break the entire orchestration.
                _logger.LogWarning(ex, "Agent {AgentType} failed – continuing with remaining agents", agent.AgentType);
            }
        }

        // Merge and rank the results.
        var fused = await FuseAndRankResultsAsync(aggregatedResults, query, searchOptions);
        return fused;
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
            // Build a concise prompt to stay within context limits.
            var snippets = results.Take(10) // limit number of snippets to avoid prompt bloat
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

            // Utilise the existing OpenAI client – it already implements retry and resilience patterns.
            var answer = await _openAIClient.GetChatCompletionAsync("gpt-4o-mini", prompt);
            return answer;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate response via Semantic Kernel / OpenAI");
            throw;
        }
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
        var queryEmbedding = await _openAIClient.GetEmbeddingAsync("text-embedding-3-large", query);

        // Generate embeddings for each candidate result (truncate content to keep costs low).
        var contents = results.Select(r => Truncate(r.Content, 1024)).ToArray();
        var resultEmbeddings = await _openAIClient.GetEmbeddingsAsync("text-embedding-3-large", contents);

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