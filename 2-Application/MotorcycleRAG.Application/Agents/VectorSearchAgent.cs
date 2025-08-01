using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Core.Interfaces;
using MotorcycleRAG.Core.Models;

namespace MotorcycleRAG.Core.Agents;

/// <summary>
/// Vector search agent implementing hybrid search combining keyword and semantic search
/// </summary>
public class VectorSearchAgent : ISearchAgent
{
    private readonly IAzureSearchClient _searchClient;
    private readonly IAzureOpenAIClient _openAIClient;
    private readonly SearchConfiguration _searchConfig;
    private readonly ILogger<VectorSearchAgent> _logger;

    public SearchAgentType AgentType => SearchAgentType.VectorSearch;

    public VectorSearchAgent(
        IAzureSearchClient searchClient,
        IAzureOpenAIClient openAIClient,
        IOptions<SearchConfiguration> searchConfig,
        ILogger<VectorSearchAgent> logger)
    {
        _searchClient = searchClient ?? throw new ArgumentNullException(nameof(searchClient));
        _openAIClient = openAIClient ?? throw new ArgumentNullException(nameof(openAIClient));
        _searchConfig = searchConfig?.Value ?? throw new ArgumentNullException(nameof(searchConfig));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Execute hybrid search combining keyword and semantic search
    /// </summary>
    public async Task<SearchResult[]> SearchAsync(string query, SearchOptions options)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            _logger.LogWarning("Empty query provided to VectorSearchAgent");
            return Array.Empty<SearchResult>();
        }

        try
        {
            _logger.LogInformation("Executing vector search for query: {Query}", query);
            var startTime = DateTime.UtcNow;

            // Step 1: Generate query embedding for semantic search
            var queryEmbedding = await GenerateQueryEmbeddingAsync(query);

            // Step 2: Execute hybrid search (keyword + semantic)
            var rawResults = await ExecuteHybridSearchAsync(query, queryEmbedding, options);

            // Step 3: Apply result ranking and filtering
            var rankedResults = ApplyRankingAndFiltering(rawResults, options);

            // Step 4: Enhance results with metadata and highlights
            var enhancedResults = EnhanceSearchResults(rankedResults, query, options);

            var searchDuration = DateTime.UtcNow - startTime;
            _logger.LogInformation("Vector search completed in {Duration}ms with {ResultCount} results", 
                searchDuration.TotalMilliseconds, enhancedResults.Length);

            return enhancedResults;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing vector search for query: {Query}", query);
            throw new InvalidOperationException($"Vector search failed: {ex.Message}", ex);
        }
    }

    #region Private Methods

    /// <summary>
    /// Generate embedding for the search query
    /// </summary>
    private async Task<float[]> GenerateQueryEmbeddingAsync(string query)
    {
        try
        {
            _logger.LogDebug("Generating embedding for query: {Query}", query);
            
            // Enhanced query for better embeddings
            var enhancedQuery = EnhanceQueryForEmbedding(query);
            var embedding = await _openAIClient.GetEmbeddingAsync("text-embedding-3-large", enhancedQuery);
            
            _logger.LogDebug("Successfully generated embedding of {Dimensions} dimensions", embedding.Length);
            return embedding;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate embedding for query, falling back to keyword search only");
            return Array.Empty<float>();
        }
    }

    /// <summary>
    /// Execute hybrid search combining keyword and vector search
    /// </summary>
    private async Task<SearchResult[]> ExecuteHybridSearchAsync(
        string query, 
        float[] queryEmbedding, 
        SearchOptions options)
    {
        var results = new List<SearchResult>();

        // Execute keyword search
        var keywordResults = await ExecuteKeywordSearchAsync(query, options);
        results.AddRange(keywordResults);

        // Execute semantic search if embedding is available
        if (queryEmbedding.Length > 0 && _searchConfig.EnableHybridSearch)
        {
            var semanticResults = await ExecuteSemanticSearchAsync(query, queryEmbedding, options);
            results.AddRange(semanticResults);
        }

        // Deduplicate results by document ID
        var deduplicatedResults = DeduplicateResults(results);

        _logger.LogDebug("Hybrid search returned {KeywordCount} keyword + {SemanticCount} semantic = {TotalCount} total results",
            keywordResults.Length, 
            queryEmbedding.Length > 0 ? results.Count - keywordResults.Length : 0,
            deduplicatedResults.Count);

        return deduplicatedResults.ToArray();
    }

    /// <summary>
    /// Execute keyword-based search
    /// </summary>
    private async Task<SearchResult[]> ExecuteKeywordSearchAsync(string query, SearchOptions options)
    {
        try
        {
            _logger.LogDebug("Executing keyword search for: {Query}", query);

            // Build search parameters for keyword search
            var maxResults = Math.Min(options.MaxResults, _searchConfig.MaxSearchResults);
            
            // Execute search through Azure Search client
            var results = await _searchClient.SearchAsync(query, maxResults);
            
            // Convert to SearchResult format with keyword search metadata
            var searchResults = results.Select(result => new SearchResult
            {
                Id = result.Id,
                Content = result.Content,
                RelevanceScore = result.RelevanceScore * 0.7f, // Weight keyword search at 70%
                Source = new SearchSource
                {
                    AgentType = SearchAgentType.VectorSearch,
                    SourceName = "Azure AI Search - Keyword",
                    DocumentId = result.Id,
                    SourceUrl = result.Source.SourceUrl,
                    LastUpdated = result.GeneratedAt
                },
                Metadata = new Dictionary<string, object>(result.Metadata)
                {
                    ["searchType"] = "keyword",
                    ["originalScore"] = result.RelevanceScore
                },
                GeneratedAt = DateTime.UtcNow,
                Highlights = ExtractHighlights(result.Content, query)
            }).ToArray();

            _logger.LogDebug("Keyword search returned {ResultCount} results", searchResults.Length);
            return searchResults;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Keyword search failed for query: {Query}", query);
            return Array.Empty<SearchResult>();
        }
    }

    /// <summary>
    /// Execute semantic vector search
    /// </summary>
    private async Task<SearchResult[]> ExecuteSemanticSearchAsync(
        string query, 
        float[] queryEmbedding, 
        SearchOptions options)
    {
        try
        {
            _logger.LogDebug("Executing semantic search for: {Query}", query);

            // For now, use a simplified semantic search simulation
            // In a real implementation, this would use Azure AI Search vector search capabilities
            var results = await SimulateSemanticSearchAsync(query, queryEmbedding, options);

            _logger.LogDebug("Semantic search returned {ResultCount} results", results.Length);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Semantic search failed for query: {Query}", query);
            return Array.Empty<SearchResult>();
        }
    }

    /// <summary>
    /// Simulate semantic search (placeholder for real vector search implementation)
    /// </summary>
    private async Task<SearchResult[]> SimulateSemanticSearchAsync(
        string query, 
        float[] queryEmbedding, 
        SearchOptions options)
    {
        // This is a placeholder implementation
        // In production, this would use Azure AI Search vector search capabilities
        await Task.Delay(100); // Simulate search time

        var semanticResults = new List<SearchResult>();
        
        // Generate some semantic search results with motorcycle-specific content
        var motorcycleTerms = ExtractMotorcycleTerms(query);
        
        for (int i = 0; i < Math.Min(3, options.MaxResults); i++)
        {
            var relevanceScore = 0.9f - (i * 0.1f);
            
            semanticResults.Add(new SearchResult
            {
                Id = $"semantic_{Guid.NewGuid()}",
                Content = GenerateSemanticContent(query, motorcycleTerms, i),
                RelevanceScore = relevanceScore * 1.1f, // Weight semantic search higher
                Source = new SearchSource
                {
                    AgentType = SearchAgentType.VectorSearch,
                    SourceName = "Azure AI Search - Semantic",
                    DocumentId = $"semantic_doc_{i}",
                    LastUpdated = DateTime.UtcNow
                },
                Metadata = new Dictionary<string, object>
                {
                    ["searchType"] = "semantic",
                    ["embeddingDimensions"] = queryEmbedding.Length,
                    ["motorcycleTerms"] = motorcycleTerms
                },
                GeneratedAt = DateTime.UtcNow,
                Highlights = ExtractHighlights(GenerateSemanticContent(query, motorcycleTerms, i), query)
            });
        }

        return semanticResults.ToArray();
    }

    /// <summary>
    /// Apply ranking and filtering logic to search results
    /// </summary>
    private SearchResult[] ApplyRankingAndFiltering(SearchResult[] results, SearchOptions options)
    {
        _logger.LogDebug("Applying ranking and filtering to {ResultCount} results", results.Length);

        var filteredResults = results
            // Apply minimum relevance score filter
            .Where(r => r.RelevanceScore >= options.MinRelevanceScore)
            // Apply custom filters if provided
            .Where(r => ApplyCustomFilters(r, options.Filters))
            // Sort by relevance score (descending)
            .OrderByDescending(r => r.RelevanceScore)
            // Apply boost for recency if enabled
            .Select(r => ApplyRecencyBoost(r))
            // Take maximum results
            .Take(options.MaxResults)
            .ToArray();

        _logger.LogDebug("Filtered and ranked results: {FilteredCount}/{OriginalCount}", 
            filteredResults.Length, results.Length);

        return filteredResults;
    }

    /// <summary>
    /// Enhance search results with additional metadata and formatting
    /// </summary>
    private SearchResult[] EnhanceSearchResults(SearchResult[] results, string query, SearchOptions options)
    {
        return results.Select(result =>
        {
            // Add search-specific metadata
            if (options.IncludeMetadata)
            {
                result.Metadata["searchQuery"] = query;
                result.Metadata["searchTimestamp"] = DateTime.UtcNow;
                result.Metadata["agentType"] = AgentType.ToString();
            }

            // Ensure highlights are present
            if (result.Highlights.Count == 0)
            {
                result.Highlights = ExtractHighlights(result.Content, query);
            }

            return result;
        }).ToArray();
    }

    /// <summary>
    /// Remove duplicate results based on document ID
    /// </summary>
    private List<SearchResult> DeduplicateResults(List<SearchResult> results)
    {
        var seen = new HashSet<string>();
        var deduplicatedResults = new List<SearchResult>();

        foreach (var result in results.OrderByDescending(r => r.RelevanceScore))
        {
            var key = result.Source.DocumentId ?? result.Id;
            
            if (!seen.Contains(key))
            {
                seen.Add(key);
                deduplicatedResults.Add(result);
            }
            else
            {
                // Keep the higher scoring result
                var existingResult = deduplicatedResults.FirstOrDefault(r => 
                    (r.Source.DocumentId ?? r.Id) == key);
                
                if (existingResult != null && result.RelevanceScore > existingResult.RelevanceScore)
                {
                    deduplicatedResults.Remove(existingResult);
                    deduplicatedResults.Add(result);
                }
            }
        }

        return deduplicatedResults;
    }

    /// <summary>
    /// Enhance query text for better embedding generation
    /// </summary>
    private string EnhanceQueryForEmbedding(string query)
    {
        // Add motorcycle context to improve embedding quality
        var motorcycleTerms = ExtractMotorcycleTerms(query);
        
        if (motorcycleTerms.Any())
        {
            return $"Motorcycle {query} specifications features performance";
        }
        
        return $"Motorcycle {query}";
    }

    /// <summary>
    /// Extract motorcycle-related terms from query
    /// </summary>
    private List<string> ExtractMotorcycleTerms(string query)
    {
        var motorcycleKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Honda", "Yamaha", "Kawasaki", "Suzuki", "Ducati", "BMW", "KTM", "Aprilia",
            "CBR", "YZF", "ZX", "GSX", "Panigale", "R1", "R6", "Ninja", "Fireblade",
            "engine", "horsepower", "torque", "displacement", "cc", "motorcycle", "bike",
            "sport", "touring", "cruiser", "naked", "adventure", "superbike"
        };

        return query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(term => motorcycleKeywords.Contains(term))
            .ToList();
    }

    /// <summary>
    /// Generate content for semantic search results
    /// </summary>
    private string GenerateSemanticContent(string query, List<string> motorcycleTerms, int index)
    {
        var templates = new[]
        {
            $"Detailed specifications for {string.Join(" ", motorcycleTerms)} including performance metrics, engine details, and technical features related to {query}.",
            $"Comprehensive information about {string.Join(" ", motorcycleTerms)} covering {query} specifications, maintenance procedures, and operational characteristics.",
            $"Technical documentation for {string.Join(" ", motorcycleTerms)} featuring {query} details, performance data, and engineering specifications."
        };

        return templates[index % templates.Length];
    }

    /// <summary>
    /// Extract text highlights from content based on query
    /// </summary>
    private List<string> ExtractHighlights(string content, string query)
    {
        var highlights = new List<string>();
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var word in words)
        {
            var index = content.IndexOf(word, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                var start = Math.Max(0, index - 50);
                var length = Math.Min(100, content.Length - start);
                var highlight = content.Substring(start, length);
                
                highlights.Add($"...{highlight}...");
            }
        }

        return highlights.Take(3).ToList(); // Limit to 3 highlights
    }

    /// <summary>
    /// Apply custom filters to search results
    /// </summary>
    private bool ApplyCustomFilters(SearchResult result, Dictionary<string, object> filters)
    {
        if (filters.Count == 0) return true;

        foreach (var filter in filters)
        {
            if (result.Metadata.TryGetValue(filter.Key, out var value))
            {
                if (!value.Equals(filter.Value))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Apply recency boost to results
    /// </summary>
    private SearchResult ApplyRecencyBoost(SearchResult result)
    {
        if (result.Source.LastUpdated != default)
        {
            var daysSinceUpdate = (DateTime.UtcNow - result.Source.LastUpdated).TotalDays;
            if (daysSinceUpdate < 30)
            {
                // Boost recent content by up to 10%
                var boost = Math.Max(0.01f, 0.1f - (float)(daysSinceUpdate / 300));
                result.RelevanceScore = Math.Min(1.0f, result.RelevanceScore + boost);
                result.Metadata["recencyBoost"] = boost;
            }
        }

        return result;
    }

    #endregion
} 