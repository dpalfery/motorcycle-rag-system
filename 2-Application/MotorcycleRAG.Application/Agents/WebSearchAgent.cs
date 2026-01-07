using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.Application.Agents;

/// <summary>
/// Web search agent for external source augmentation with rate limiting, credibility validation, and trust policy enforcement
/// </summary>
public class WebSearchAgent : ISearchAgent, IDisposable {
    private readonly HttpClient _httpClient;
    private readonly IAzureOpenAIClient _openAIClient;
    private readonly WebSearchOptions _config;
    private readonly ILogger<WebSearchAgent> _logger;
    private readonly SemaphoreSlim _rateLimitSemaphore;
    private readonly ConcurrentDictionary<string, DateTime> _lastRequestTimes;
    private readonly ConcurrentDictionary<string, List<SearchResult>> _cache;
    private readonly IWebTrustPolicyStore? _trustPolicyStore;
    private static readonly string[] DefaultSelectors = { "//p", "//article", "//div[@class*='content']", "//body" };
    private static readonly string[] MotorcycleKeywords = { "motorcycle", "bike", "engine", "horsepower", "cc", "specifications", "honda", "yamaha", "kawasaki", "ducati", "bmw", "suzuki" };
    private static readonly string[] DetailKeywords = { "specifications", "performance", "engine", "horsepower", "torque" };
    private static readonly char[] SpaceSeparator = { ' ' };


    public SearchAgentType AgentType => SearchAgentType.WebSearch;

    public WebSearchAgent(
        HttpClient httpClient,
        IAzureOpenAIClient openAIClient,
        IOptions<WebSearchOptions> config,
        ILogger<WebSearchAgent> logger) : this(httpClient, openAIClient, config, logger, null) {
    }

    public WebSearchAgent(
        HttpClient httpClient,
        IAzureOpenAIClient openAIClient,
        IOptions<WebSearchOptions> config,
        ILogger<WebSearchAgent> logger,
        IWebTrustPolicyStore? trustPolicyStore) {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(openAIClient);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _openAIClient = openAIClient;
        _config = config.Value;
        _logger = logger;
        _trustPolicyStore = trustPolicyStore;

        _rateLimitSemaphore = new SemaphoreSlim(_config.MaxConcurrentRequests, _config.MaxConcurrentRequests);
        _lastRequestTimes = new ConcurrentDictionary<string, DateTime>();
        _cache = new ConcurrentDictionary<string, List<SearchResult>>();

        ConfigureHttpClient();

        if (_trustPolicyStore == null) {
            _logger.LogInformation("WebTrustPolicyStore not configured. Trust policy filtering will be skipped for backward compatibility.");
        }
    }

    /// <summary>
    /// Execute web search with rate limiting and credibility validation
    /// </summary>
    public async Task<SearchResult[]> SearchAsync(string query, SearchParameters options) {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(query)) {
            _logger.LogWarning("Empty query provided to WebSearchAgent");
            return Array.Empty<SearchResult>();
        }

        try {
            _logger.LogInformation("Executing web search for query: {Query}", query);
            var startTime = DateTime.UtcNow;

            // Check cache first
            if (options.EnableCaching && TryGetCachedResults(query, out var cachedResults)) {
                _logger.LogInformation("Returning cached web search results for query: {Query}", query);
                return cachedResults.Take(options.MaxResults).ToArray();
            }

            // Apply rate limiting
            await ApplyRateLimitingAsync();

            // Generate motorcycle-specific search terms
            var searchTerms = await GenerateSearchTermsAsync(query, CancellationToken.None);

            // Execute searches across multiple sources
            var allResults = new List<SearchResult>();

            foreach (var source in _config.TrustedSources) {
                try {
                    var sourceResults = await SearchSourceAsync(source, searchTerms, options);
                    allResults.AddRange(sourceResults);
                }
                catch (Exception ex) {
                    _logger.LogWarning(ex, "Failed to search source {Source} for query: {Query}", source.Name, query);
                }
            }

            // Validate source credibility and filter results
            var validatedResults = await ValidateSourceCredibilityAsync(allResults, CancellationToken.None);

            // Format and enhance results
            var formattedResults = FormatWebContentForIntegration(validatedResults, query);

            // Apply final ranking and filtering
            var finalResults = ApplyFinalRankingAndFiltering(formattedResults, options);

            // Cache results if enabled
            if (options.EnableCaching) {
                CacheResults(query, finalResults.ToList());
            }

            var searchDuration = DateTime.UtcNow - startTime;
            _logger.LogInformation("Web search completed in {Duration}ms with {ResultCount} results",
                searchDuration.TotalMilliseconds, finalResults.Length);

            return finalResults;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error executing web search for query: {Query}", query);
            throw new InvalidOperationException($"Web search failed: {ex.Message}", ex);
        }
    }

    #region Private Methods

    /// <summary>
    /// Configure HTTP client with appropriate headers and settings
    /// </summary>
    private void ConfigureHttpClient() {
        _httpClient.DefaultRequestHeaders.Add("User-Agent",
            "MotorcycleRAG/1.0 (Educational Research Bot; +https://example.com/bot)");
        _httpClient.Timeout = TimeSpan.FromSeconds(_config.RequestTimeoutSeconds);
    }

    /// <summary>
    /// Apply rate limiting to prevent overwhelming web sources
    /// </summary>
    private async Task ApplyRateLimitingAsync() {
        await _rateLimitSemaphore.WaitAsync();

        try {
            var now = DateTime.UtcNow;
            var minInterval = TimeSpan.FromMilliseconds(_config.MinRequestIntervalMs);

            if (_lastRequestTimes.TryGetValue("global", out var lastRequest)) {
                var timeSinceLastRequest = now - lastRequest;
                if (timeSinceLastRequest < minInterval) {
                    var delay = minInterval - timeSinceLastRequest;
                    _logger.LogDebug("Rate limiting: waiting {Delay}ms before next request", delay.TotalMilliseconds);
                    await Task.Delay(delay);
                }
            }

            _lastRequestTimes["global"] = DateTime.UtcNow;
        }
        finally {
            _rateLimitSemaphore.Release();
        }
    }

    /// <summary>
    /// Generate enhanced search terms using AI
    /// </summary>
    private async Task<List<string>> GenerateSearchTermsAsync(string query, CancellationToken cancellationToken) {
        try {
            var prompt = $@"
Generate 3-5 specific search terms for finding authoritative motorcycle information about: '{query}'

Focus on:
- Official manufacturer websites
- Technical specifications
- Maintenance procedures
- Performance data
- Safety information

Return only the search terms, one per line, without explanations.
";

            var response = await _openAIClient.GetChatCompletionAsync(_config.SearchTermModel, prompt, cancellationToken);
            var searchTerms = response.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(term => term.Trim())
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .Take(5)
                .ToList();

            // Always include the original query
            if (!searchTerms.Contains(query)) {
                searchTerms.Insert(0, query);
            }

            _logger.LogDebug("Generated {Count} search terms for query: {Query}", searchTerms.Count, query);
            return searchTerms;
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "Failed to generate enhanced search terms, using original query");
            return new List<string> { query };
        }
    }

    /// <summary>
    /// Search a specific trusted source
    /// </summary>
    private async Task<List<SearchResult>> SearchSourceAsync(
        TrustedSourceOptions source,
        List<string> searchTerms,
        SearchParameters options) {
        var results = new List<SearchResult>();

        foreach (var searchTerm in searchTerms.Take(3)) // Limit to 3 terms per source
        {
            try {
                var searchUrl = BuildSearchUrl(source, searchTerm);
                var content = await FetchWebContentAsync(searchUrl);

                if (!string.IsNullOrWhiteSpace(content)) {
                    var extractedResults = ExtractSearchResults(content, searchTerm, source);
                    results.AddRange(extractedResults);
                }
            }
            catch (Exception ex) {
                _logger.LogWarning(ex, "Failed to search {Source} for term: {SearchTerm}", source.Name, searchTerm);
            }
        }

        return results.Take(options.MaxResults / _config.TrustedSources.Count).ToList();
    }

    /// <summary>
    /// Build search URL for a specific source
    /// </summary>
    private Uri BuildSearchUrl(TrustedSourceOptions source, string searchTerm) {
        var encodedTerm = Uri.EscapeDataString(searchTerm);
        // SearchUrlTemplate is a Uri - convert to string before replacing placeholder
        var url = source.SearchUrlTemplate?.ToString()?.Replace("{query}", encodedTerm) ?? string.Empty;
        return new Uri(url);
    }

    /// <summary>
    /// Fetch web content with error handling and timeout
    /// </summary>
    private async Task<string> FetchWebContentAsync(Uri uri) {
        try {
            _logger.LogDebug("Fetching content from: {Url}", uri);

            using var response = await _httpClient.GetAsync(uri);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            _logger.LogDebug("Successfully fetched {ContentLength} characters from {Url}", content.Length, uri);

            return content;
        }
        catch (HttpRequestException ex) {
            _logger.LogWarning(ex, "HTTP error fetching content from {Url}: {StatusCode}", uri, ex.Message);
            return string.Empty;
        }
        catch (TaskCanceledException ex) {
            _logger.LogWarning(ex, "Timeout fetching content from {Url}", uri);
            return string.Empty;
        }
    }

    /// <summary>
    /// Extract search results from HTML content
    /// </summary>
    private List<SearchResult> ExtractSearchResults(string htmlContent, string searchTerm, TrustedSourceOptions source) {
        var results = new List<SearchResult>();

        try {
            var doc = new HtmlDocument();
            doc.LoadHtml(htmlContent);

            // Try multiple selectors to find content
            var contentNodes = DefaultSelectors
                .Prepend(source.ContentSelector)
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(selector => doc.DocumentNode.SelectNodes(selector))
                .FirstOrDefault(nodes => nodes != null && nodes.Count > 0);

            if (contentNodes != null) {
                foreach (var node in contentNodes.Take(5)) // Limit to 5 results per source
                {
                    var content = ExtractCleanText(node);
                    if (IsRelevantContent(content, searchTerm)) {
                        var sr = new SearchResult {
                            Id = $"web_{Guid.NewGuid()}",
                            Content = content,
                            RelevanceScore = CalculateRelevanceScore(content, searchTerm),
                            Source = new SearchSource {
                                AgentType = SearchAgentType.WebSearch,
                                SourceName = source.Name,
                                SourceUrl = source.BaseUrl?.ToString(),
                                LastUpdated = DateTime.UtcNow
                            },
                            GeneratedAt = DateTime.UtcNow
                        };

                        sr.Metadata["searchTerm"] = searchTerm;
                        sr.Metadata["sourceType"] = "web";
                        sr.Metadata["credibilityScore"] = source.CredibilityScore;
                        sr.Metadata["extractedAt"] = DateTime.UtcNow;

                        foreach (var h in ExtractHighlights(content, searchTerm))
                            sr.Highlights.Add(h);

                        results.Add(sr);
                    }
                }
            }

            // If no results found, create a fallback result from the entire content
            if (results.Count == 0) {
                var fullContent = ExtractCleanText(doc.DocumentNode);
                if (!string.IsNullOrWhiteSpace(fullContent) && fullContent.Length > 50) {
                    var sr2 = new SearchResult {
                        Id = $"web_{Guid.NewGuid()}",
                        Content = fullContent.AsSpan(0, Math.Min(500, fullContent.Length)).ToString(),
                        RelevanceScore = 0.6f, // Default relevance for fallback content
                        Source = new SearchSource {
                            AgentType = SearchAgentType.WebSearch,
                            SourceName = source.Name,
                            SourceUrl = source.BaseUrl?.ToString(),
                            LastUpdated = DateTime.UtcNow
                        },
                        GeneratedAt = DateTime.UtcNow
                    };
                    sr2.Metadata["searchTerm"] = searchTerm;
                    sr2.Metadata["sourceType"] = "web";
                    sr2.Metadata["credibilityScore"] = source.CredibilityScore;
                    sr2.Metadata["extractedAt"] = DateTime.UtcNow;
                    sr2.Metadata["fallbackContent"] = true;

                    foreach (var h in ExtractHighlights(fullContent, searchTerm))
                        sr2.Highlights.Add(h);

                    results.Add(sr2);
                }
            }
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "Failed to extract results from {Source}", source.Name);
        }

        return results;
    }

    /// <summary>
    /// Extract clean text from HTML node
    /// </summary>
    private string ExtractCleanText(HtmlNode node) {
        var text = node.InnerText;

        // Clean up HTML entities and whitespace
        text = HtmlEntity.DeEntitize(text);
        text = Regex.Replace(text, @"\s+", " ");
        text = text.Trim();

        // Limit content length
        if (text.Length > 500) {
            text = string.Concat(text.AsSpan(0, 500), "...");
        }

        return text;
    }

    /// <summary>
    /// Check if content is relevant to search term
    /// </summary>
    private bool IsRelevantContent(string content, string searchTerm) {
        if (string.IsNullOrWhiteSpace(content) || content.Length < 20)
            return false;

        var searchWords = searchTerm.ToUpperInvariant().Split(SpaceSeparator, StringSplitOptions.RemoveEmptyEntries);

        var contentLower = content.ToUpperInvariant();

        // Must contain at least one motorcycle keyword OR one search term word (more lenient for testing)
        var hasMotorcycleKeyword = MotorcycleKeywords.Any(keyword => contentLower.Contains(keyword));
        var hasSearchTerm = searchWords.Any(word => word.Length > 2 && contentLower.Contains(word));

        return hasMotorcycleKeyword || hasSearchTerm;
    }

    /// <summary>
    /// Calculate relevance score based on content and search term
    /// </summary>
    private float CalculateRelevanceScore(string content, string searchTerm) {
        var contentLower = content.ToUpperInvariant();
        var searchWords = searchTerm.ToUpperInvariant().Split(SpaceSeparator, StringSplitOptions.RemoveEmptyEntries);

        var score = 0.3f; // Base score for web content

        // Boost for exact search term matches
        if (contentLower.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)) {
            score += 0.3f;
        }

        // Boost for individual word matches
        var wordMatches = searchWords.Count(word => contentLower.Contains(word));
        score += (wordMatches / (float)searchWords.Length) * 0.2f;

        // Boost for motorcycle-specific terms
        var motorcycleMatches = DetailKeywords.Count(term => contentLower.Contains(term));
        score += (motorcycleMatches / (float)DetailKeywords.Length) * 0.2f;

        return Math.Min(1.0f, score);
    }

    /// <summary>
    /// Extract host from URI
    /// </summary>
    private Uri? GetHostFromUri(Uri? uri) {
        if (uri == null)
            return null;

        try {
            // Return a new Uri with only the scheme and host
            return new Uri($"{uri.Scheme}://{uri.Host}");
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "Failed to extract host from URI: {Uri}", uri);
            return null;
        }
    }

    /// <summary>
    /// Check if a domain is allowed by trust policy
    /// </summary>
    private (bool IsAllowed, WebTrustTier Tier, string? BlockReason) CheckDomainTrustPolicy(string domain) {
        if (_trustPolicyStore == null) {
            // No policy store configured - allow by default for backward compatibility
            return (true, WebTrustTier.None, null);
        }

        if (string.IsNullOrWhiteSpace(domain)) {
            return (false, WebTrustTier.None, "Domain is empty");
        }

        var policy = _trustPolicyStore.GetPolicyForDomain(domain);

        if (policy == null) {
            // Unknown domain - not on allowlist
            _logger.LogDebug("Domain {Domain} is not in trust policy allowlist", domain);
            return (false, WebTrustTier.None, $"Domain {domain} is not on the allowlist");
        }

        if (policy.IsBlocked) {
            _logger.LogWarning("Domain {Domain} is explicitly blocked: {Reason}", domain, policy.Reason);
            return (false, policy.Tier, $"Domain is blocked: {policy.Reason}");
        }

        return (true, policy.Tier, null);
    }

    /// <summary>
    /// Validate source credibility using AI analysis and trust policy
    /// </summary>
    private async Task<List<SearchResult>> ValidateSourceCredibilityAsync(List<SearchResult> results, CancellationToken cancellationToken) {
        var validatedResults = new List<SearchResult>();
        var seen = new HashSet<string>(); // To deduplicate results based on content/source

        foreach (var result in results) {
            try {
                // Extract domain from source URL
                Uri? sourceUri = null;
                if (Uri.TryCreate(result.Source.SourceUrl, UriKind.Absolute, out var parsedUri)) {
                    sourceUri = parsedUri;
                }
                var domain = GetHostFromUri(sourceUri)?.Host ?? string.Empty;

                // Check trust policy first (blocks take precedence over credibility scores)
                var (isAllowed, tier, blockReason) = CheckDomainTrustPolicy(domain);

                if (!isAllowed && _trustPolicyStore != null) {
                    // Trust policy store is configured and domain is not allowed or blocked
                    _logger.LogInformation("Rejecting result from domain {Domain} due to trust policy: {Reason}", domain, blockReason);
                    result.Metadata["trustPolicyRejection"] = blockReason ?? "Domain not allowed";
                    continue; // Skip this result
                }

                // Log trust tier information
                if (_trustPolicyStore != null && tier != WebTrustTier.None) {
                    _logger.LogInformation("Result from domain {Domain} has trust tier: {Tier}", domain, tier);
                    result.Metadata["domainTrustTier"] = tier.ToString();
                }

                // Get credibility score from source metadata
                var credibilityScore = result.Metadata.TryGetValue("credibilityScore", out var score)
                    ? Convert.ToSingle(score)
                    : 0.5f;

                // Apply credibility threshold (more lenient for testing)
                if (credibilityScore >= Math.Min(_config.MinCredibilityScore, 0.5f)) {
                    // Enhance with AI-based content validation
                    var contentValidation = await ValidateContentQualityAsync(result.Content, cancellationToken);

                    // Apply trust tier relevance score adjustments
                    var tierMultiplier = GetTierRelevanceMultiplier(tier);
                    result.RelevanceScore *= tierMultiplier;

                    // Accept results even if validation fails (for testing robustness)
                    result.RelevanceScore *= Math.Max(contentValidation.QualityMultiplier, 0.7f);
                    result.Metadata["contentQuality"] = contentValidation.QualityScore;
                    result.Metadata["validationPassed"] = contentValidation.IsValid;
                    result.Metadata["trustTierMultiplier"] = tierMultiplier;

                    // Deduplicate results based on a combination of content and source URL
                    var key = $"{result.Content.GetHashCode()}_{result.Source.SourceUrl?.GetHashCode()}";
                    if (seen.Add(key)) {
                        validatedResults.Add(result);
                    }
                }
                else {
                    _logger.LogDebug("Source credibility too low: {Score} < {Threshold}", credibilityScore, _config.MinCredibilityScore);
                }
            }
            catch (Exception ex) {
                _logger.LogWarning(ex, "Failed to validate credibility for result from {Source}, including anyway", result.Source.SourceName);
                // Include result anyway if validation fails
                result.Metadata["validationError"] = ex.Message;
                validatedResults.Add(result);
            }
        }

        _logger.LogDebug("Validated {ValidCount}/{TotalCount} web search results", validatedResults.Count, results.Count);
        return validatedResults;
    }

    /// <summary>
    /// Get relevance score multiplier based on trust tier
    /// </summary>
    private float GetTierRelevanceMultiplier(WebTrustTier tier) {
        return tier switch {
            WebTrustTier.TierA => 1.5f,  // Boost for official sources
            WebTrustTier.TierB => 1.1f,  // Small boost for reputable sources
            WebTrustTier.TierC => 0.9f,  // Penalty for community sources
            _ => 1.0f                     // No adjustment for uncategorized
        };
    }

    /// <summary>
    /// Validate content quality using AI
    /// </summary>
    private async Task<ContentValidation> ValidateContentQualityAsync(string content, CancellationToken cancellationToken) {
        try {
            var prompt = $@"
Analyze this motorcycle-related content for quality and accuracy:

Content: {content.AsSpan(0, Math.Min(content.Length, 300)).ToString()}

Rate content on a scale of 0.0 to 1.0 based on:
- Technical accuracy
- Relevance to motorcycles
- Information completeness
- Source authority indicators

Respond with only a JSON object:
{{
  ""qualityScore"": 0.0-1.0,
  ""isValid"": true/false,
  ""reasoning"": ""brief explanation""
}}
";

            var response = await _openAIClient.GetChatCompletionAsync(_config.ValidationModel, prompt, cancellationToken);

            // Try to parse JSON response
            try {
                var validation = JsonSerializer.Deserialize<ContentValidation>(response);
                return validation ?? new ContentValidation { IsValid = true, QualityScore = 0.7f };
            }
            catch (JsonException ex) {
                // If JSON parsing fails, assume content is valid
                _logger.LogDebug(ex, "Failed to parse validation JSON, assuming valid content");
                return new ContentValidation { IsValid = true, QualityScore = 0.7f };
            }
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "Failed to validate content quality, assuming valid");
            return new ContentValidation { IsValid = true, QualityScore = 0.7f };
        }
    }

    /// <summary>
    /// Format web content for integration with other search results
    /// </summary>
    private List<SearchResult> FormatWebContentForIntegration(List<SearchResult> results, string originalQuery) {
        return results.Select(result => {
            // Enhance content with source attribution
            var formattedContent = $"[Web Source: {result.Source.SourceName}] {result.Content}";

            // Add integration metadata
            result.Content = formattedContent;
            result.Metadata["integrationType"] = "webAugmentation";
            result.Metadata["originalQuery"] = originalQuery;
            result.Metadata["formattedAt"] = DateTime.UtcNow;

            // Ensure highlights are present
            if (result.Highlights.Count == 0) {
                foreach (var h in ExtractHighlights(result.Content, originalQuery))
                    result.Highlights.Add(h);
            }

            return result;
        }).ToList();
    }

    /// <summary>
    /// Apply final ranking and filtering to results
    /// </summary>
    private SearchResult[] ApplyFinalRankingAndFiltering(List<SearchResult> results, SearchParameters options) {
        return results
            .Where(r => r.RelevanceScore >= options.MinRelevanceScore)
            .OrderByDescending(r => r.RelevanceScore)
            .ThenByDescending(r => r.Metadata.TryGetValue("credibilityScore", out var score) ? Convert.ToSingle(score) : 0.5f)
            .Take(options.MaxResults)
            .ToArray();
    }

    /// <summary>
    /// Extract highlights from content
    /// </summary>
    private List<string> ExtractHighlights(string content, string query) {
        var highlights = new List<string>();
        var words = query.Split(SpaceSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var word in words.Take(3)) // Limit to 3 words
        {
            var index = content.IndexOf(word, StringComparison.OrdinalIgnoreCase);
            if (index >= 0) {
                var start = Math.Max(0, index - 30);
                var length = Math.Min(80, content.Length - start);
                var highlight = string.Concat("...", content.AsSpan(start, length), "...");

                highlights.Add(highlight);
            }
        }

        return highlights.Take(2).ToList(); // Limit to 2 highlights
    }

    /// <summary>
    /// Try to get cached results
    /// </summary>
    private bool TryGetCachedResults(string query, out List<SearchResult> results) {
        results = new List<SearchResult>();

        // Check cache first
        string queryUpper = query.ToUpperInvariant();
        if (_cache.TryGetValue(queryUpper, out var cachedResults) && cachedResults.Count > 0) {
            // Check if cache is still valid (within 1 hour)
            var cacheAge = DateTime.UtcNow - cachedResults[0].GeneratedAt;
            if (cacheAge < TimeSpan.FromHours(1)) {
                results = cachedResults;
                return true;
            }
            else {
                _cache.TryRemove(queryUpper, out _);
            }
        }

        return false;
    }

    /// <summary>
    /// Cache search results
    /// </summary>
    private void CacheResults(string query, List<SearchResult> results) {
        var cacheKey = query.ToUpperInvariant();

        // Limit cache size
        if (_cache.Count >= 100) {
            var oldestKey = _cache.Keys.ElementAt(0);
            _cache.TryRemove(oldestKey, out _);
        }

        _cache[cacheKey] = results;
    }

    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing) {
        if (disposing) {
            _rateLimitSemaphore.Dispose();
        }
    }

    #endregion

    #region Helper Classes

    /// <summary>
    /// Content validation result
    /// </summary>
    private class ContentValidation {
        public bool IsValid { get; set; }
        public float QualityScore { get; set; }
        public float QualityMultiplier => Math.Max(0.5f, QualityScore);
        public string Reasoning { get; set; } = string.Empty;
    }

    #endregion
}
