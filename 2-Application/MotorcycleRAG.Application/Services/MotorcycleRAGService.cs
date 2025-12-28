#pragma warning disable CS8601 // Possible null reference assignment
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Application.Caching;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models;
using MotorcycleRAG.Domain.Models;

namespace MotorcycleRAG.Application.Services;

/// <summary>
/// Claim citation issue types
/// </summary>
public enum ClaimCitationIssueType
{
    MissingCitation,
    LowQualityCitation,
    UnverifiableClaim
}

/// <summary>
/// Claim citation issue
/// </summary>
public class ClaimCitationIssue
{
    public string Claim { get; set; } = string.Empty;
    public ClaimCitationIssueType IssueType { get; set; }
    public string Suggestion { get; set; } = string.Empty;
}

/// <summary>
/// Query refinement analysis for no-results responses
/// </summary>
public class QueryRefinementAnalysis
{
    public string OriginalQuery { get; set; } = string.Empty;
    public List<string> Suggestions { get; set; } = new();
    public List<string> ExampleQueries { get; set; } = new();
}

/// <summary>
/// Main service coordinating the complete retrieval-augmented generation (RAG) pipeline for motorcycle queries.
/// Enhanced with caching and performance optimizations.
/// </summary>
public sealed class MotorcycleRAGService : IMotorcycleRAGService
{
    private readonly IAgentOrchestrator _orchestrator;
    private readonly ILogger<MotorcycleRAGService> _logger;
    private readonly ITelemetryService _telemetryService;
    private readonly IQueryCacheService _cacheService;
    private readonly CacheConfiguration _cacheConfig;
    private readonly IAzureOpenAIClient _openAIClient;

    public MotorcycleRAGService(
        IAgentOrchestrator orchestrator,
        ILogger<MotorcycleRAGService> logger,
        ITelemetryService telemetryService,
        IQueryCacheService cacheService,
        IOptions<CacheConfiguration> cacheConfig,
        IAzureOpenAIClient openAIClient)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _telemetryService = telemetryService ?? throw new ArgumentNullException(nameof(telemetryService));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        _cacheConfig = cacheConfig?.Value ?? throw new ArgumentNullException(nameof(cacheConfig));
        _openAIClient = openAIClient ?? throw new ArgumentNullException(nameof(openAIClient));
    }

    /// <inheritdoc />
    public async Task<MotorcycleQueryResponse> SearchAsync(MotorcycleQueryRequest request)
    {
        return await QueryAsync(request);
    }

    /// <inheritdoc />
    public async Task<MotorcycleQueryResponse> QueryAsync(MotorcycleQueryRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        if (string.IsNullOrWhiteSpace(request.Query))
            throw new ArgumentException("Query cannot be null or empty", nameof(request));

        _logger.LogInformation("Processing motorcycle RAG query: {Query}", request.Query);

        var stopwatch = Stopwatch.StartNew();
        var queryId = Guid.NewGuid().ToString("N");

        // 1. Check cache first if enabled
        MotorcycleQueryResponse? cachedResponse = null;
        string? cacheKey = null;

        if (_cacheConfig.EnableCaching)
        {
            cacheKey = _cacheService.GenerateCacheKey(request);
            cachedResponse = await _cacheService.GetAsync(cacheKey);

            if (cachedResponse != null)
            {
                stopwatch.Stop();

                // Update cached response with new query ID and timestamp
                cachedResponse.QueryId = queryId;
                cachedResponse.GeneratedAt = DateTime.UtcNow;

                if (cachedResponse.Metrics != null)
                {
                    var cachedMetrics = cachedResponse!.Metrics;
                    cachedMetrics.ProcessingTimeMs = (int)stopwatch.ElapsedMilliseconds;
                    cachedMetrics.CacheHit = true;
                }

                _logger.LogInformation("Cache hit for query. Duration: {Duration}ms", stopwatch.ElapsedMilliseconds);

                // Track telemetry for cached response
                _telemetryService.TrackQuery(queryId, request.Query, stopwatch.Elapsed,
                    cachedResponse.Sources?.Length ?? 0, cachedResponse.Metrics?.EstimatedCost ?? 0);

                return cachedResponse;
            }
        }

        // 2. Build a lightweight search context from the incoming request.
        var context = new SearchContext
        {
            SessionId = request.Context?.SessionId ?? Guid.NewGuid().ToString(),
            Preferences = request.Preferences,
            QueryContext = request.Context
        };

        // 3. Execute orchestrated search across all agents.
        var results = await _orchestrator.ExecuteSequentialSearchAsync(request.Query, context);

        // 4. Generate final natural-language response using large language model.
        var answer = await _orchestrator.GenerateResponseAsync(results, request.Query);

        // 4.5. Handle no-results case with refinement suggestions
        if (results == null || results.Length == 0 || string.IsNullOrWhiteSpace(answer))
        {
            answer = GenerateNoResultsResponseWithRefinementSuggestions(request.Query);
        }

        stopwatch.Stop();

        var estimatedCost = CalculateEstimatedCost(results ?? Array.Empty<SearchResult>(), answer ?? string.Empty);

        // 5. Populate metrics with performance data
        var metrics = new QueryMetrics
        {
            ProcessingTimeMs = (int)stopwatch.ElapsedMilliseconds,
            TotalDuration = stopwatch.Elapsed,
            ResultsFound = results?.Length ?? 0,
            CacheHit = false,
            EstimatedCost = estimatedCost
        };

        // Handle no-results case metrics
        if (results == null || results.Length == 0 || string.IsNullOrWhiteSpace(answer))
        {
            metrics.ResultsFound = 0;
            metrics.SearchPattern = new SearchPatternMetrics
            {
                VectorSearchExecuted = true,
                WebSearchExecuted = true,
                PDFSearchExecuted = true,
                VectorResultsFound = 0,
                WebResultsFound = 0,
                PDFResultsFound = 0
            };
        }

        // Extract claims and ensure citations before finalizing response
        var (finalAnswer, finalResults) = await ExtractClaimsAndEnsureCitationsAsync(answer ?? string.Empty, results ?? Array.Empty<SearchResult>(), request.Query);

        var response = new MotorcycleQueryResponse
        {
            QueryId = queryId,
            Response = finalAnswer,
            Sources = finalResults,
            Metrics = metrics,
            GeneratedAt = DateTime.UtcNow
        };

        // 6. Cache the response if enabled and meets caching criteria
        if (_cacheConfig.EnableCaching && cacheKey != null && ShouldCacheResponse(response))
        {
            var expiration = DetermineCacheExpiration(response);
            await _cacheService.SetAsync(cacheKey, response, expiration);

            _logger.LogDebug("Cached response with expiration: {Expiration}", expiration);
        }

        _logger.LogInformation("Query processed. {Results} results, duration {Duration}ms, cost: ${Cost:F4}",
            results?.Length ?? 0, stopwatch.ElapsedMilliseconds, metrics.EstimatedCost);

        // Track telemetry
        _telemetryService.TrackQuery(queryId, request.Query, stopwatch.Elapsed, results?.Length ?? 0, metrics.EstimatedCost);

        return response;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> GetHealthAsync()
    {
        var result = new HealthCheckResult
        {
            IsHealthy = true,
            Status = "OK",
            Details =
            {
                ["Timestamp"] = DateTime.UtcNow
            }
        };

        // Add cache statistics to health check
        if (_cacheConfig.EnableCaching)
        {
            try
            {
                var cacheStats = await _cacheService.GetStatisticsAsync();
                result.Details["Cache.HitRatio"] = $"{cacheStats.HitRatio:P2}";
                result.Details["Cache.TotalEntries"] = cacheStats.TotalEntries.ToString();
                result.Details["Cache.MemoryUsage"] = $"{cacheStats.TotalMemoryUsage / 1024 / 1024:F1}MB";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get cache statistics for health check");
                result.Details["Cache.Status"] = "Error";
            }
        }

        return result;
    }

    private bool ShouldCacheResponse(MotorcycleQueryResponse response)
    {
        // Cache responses that have good results and reasonable processing time
        return response.Sources?.Length > 0 &&
               response.Metrics?.ProcessingTimeMs < 30000 && // Less than 30 seconds
               !string.IsNullOrWhiteSpace(response.Response);
    }

    private TimeSpan DetermineCacheExpiration(MotorcycleQueryResponse response)
    {
        // Use longer expiration for high-quality responses
        if (response.Sources?.Length > 3 && response.Metrics?.ProcessingTimeMs < 5000)
        {
            return _cacheConfig.LongTermExpiration;
        }

        return _cacheConfig.DefaultExpiration;
    }

    private async Task<(string FinalAnswer, SearchResult[] FinalResults)> ExtractClaimsAndEnsureCitationsAsync(
        string originalAnswer, SearchResult[] originalResults, string query)
    {
        try
        {
            _logger.LogInformation("Extracting claims and ensuring citations for query: {Query}", query);

            // 1. Identify factual claims in the answer
            var claims = await IdentifyFactualClaimsAsync(originalAnswer);
            
            if (claims == null || claims.Length == 0)
            {
                _logger.LogDebug("No factual claims identified in answer");
                return (originalAnswer, originalResults);
            }

            _logger.LogDebug("Identified {ClaimCount} factual claims: {Claims}", claims.Length, string.Join(", ", claims));

            // 2. Match claims to evidence sources and create citations
            var resultsWithCitations = new List<SearchResult>();
            var citationMap = new Dictionary<string, List<Citation>>(); // claim -> citations

            foreach (var claim in claims)
            {
                var matchingSources = FindMatchingSourcesForClaim(claim, originalResults);
                
                if (matchingSources.Any())
                {
                    var citations = CreateCitationsFromSources(matchingSources, claim);
                    citationMap[claim] = citations;
                    
                    // Add to results with enhanced citations
                    foreach (var source in matchingSources)
                    {
                        if (!resultsWithCitations.Any(r => r.Id == source.Id))
                        {
                            resultsWithCitations.Add(source);
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("No evidence found for claim: {Claim}", claim);
                    citationMap[claim] = new List<Citation>();
                }
            }

            // 3. Generate final answer with proper citations
            var finalAnswer = await GenerateCitedAnswerAsync(originalAnswer, claims, citationMap);

            // 4. Ensure all results have proper citations
            var finalResults = EnsureAllResultsHaveCitations(resultsWithCitations.ToArray());

            _logger.LogInformation("Claim extraction and citation completed. Final answer length: {Length}, results with citations: {Count}",
                finalAnswer.Length, finalResults.Count(r => r.Source.Citation != null));

            // Enforce: every factual claim has citation or is qualified/omitted
            var enforcementResult = EnforceClaimCitationPolicy(finalAnswer, claims, citationMap);
            if (!enforcementResult.IsPolicyCompliant)
            {
                _logger.LogWarning("Claim-citation policy enforcement: {Issues} issues found", enforcementResult.Issues.Count);
                
                // Apply corrections to make the response policy-compliant
                finalAnswer = ApplyClaimCitationPolicyCorrections(finalAnswer, enforcementResult.Issues);
                
                _logger.LogInformation("Applied claim-citation policy corrections to ensure compliance");
            }

            return (finalAnswer, finalResults);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract claims and ensure citations");
            // Return original data on failure
            return (originalAnswer, originalResults);
        }
    }

    private async Task<string[]> IdentifyFactualClaimsAsync(string answer)
    {
        try
        {
            // Use AI to identify factual claims in the answer
            var prompt = $"""
Analyze the following answer and extract all factual claims that require citation.
Return only the factual statements as a JSON array of strings.
Do not include opinions, qualifiers, or introductory phrases.

Answer to analyze:
{answer}

Factual claims (JSON array):
""";

            var claimsJson = await _openAIClient.GetChatCompletionAsync("gpt-4o-mini", prompt, CancellationToken.None);
            
            // Parse the JSON response
            if (string.IsNullOrWhiteSpace(claimsJson))
                return Array.Empty<string>();

            // Simple JSON parsing (in production, use proper JSON parser)
            if (claimsJson.StartsWith("[") && claimsJson.EndsWith("]"))
            {
                var claims = claimsJson
                    .Trim('[', ']', ' ', '\\', '"')
                    .Split(new[] { "\",\"" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(c => c.Trim('"', ' ', '\\'))
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .ToArray();

                return claims;
            }
            
            // Fallback: extract sentences that look like facts
            return ExtractFactLikeSentences(answer);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to identify factual claims using AI, falling back to simple extraction");
            return ExtractFactLikeSentences(answer);
        }
    }

    private string[] ExtractFactLikeSentences(string answer)
    {
        // Simple fallback: extract sentences that contain numbers, specific terms, etc.
        var sentences = answer.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
                              .Select(s => s.Trim())
                              .Where(s => !string.IsNullOrWhiteSpace(s))
                              .ToArray();

        // Filter for sentences that likely contain facts
        return sentences.Where(s =>
            s.ContainsAny(new[] { " is ", " has ", " are ", " was ", " were " }) ||
            s.Any(char.IsDigit) ||
            s.Contains("cc") || s.Contains("hp") || s.Contains("kW") ||
            s.Contains("mph") || s.Contains("km/h") || s.Contains("Nm")
        ).ToArray();
    }

    private List<SearchResult> FindMatchingSourcesForClaim(string claim, SearchResult[] results)
    {
        var matchingSources = new List<SearchResult>();
        
        // Simple text matching for now (could be enhanced with semantic search)
        foreach (var result in results)
        {
            if (result.Content.Contains(claim, StringComparison.OrdinalIgnoreCase))
            {
                matchingSources.Add(result);
            }
            else
            {
                // Check if the claim contains key terms from the result
                var resultKeyTerms = ExtractKeyTerms(result.Content);
                var claimKeyTerms = ExtractKeyTerms(claim);
                
                if (resultKeyTerms.Intersect(claimKeyTerms, StringComparer.OrdinalIgnoreCase).Any())
                {
                    matchingSources.Add(result);
                }
            }
        }

        return matchingSources;
    }

    private string[] ExtractKeyTerms(string text)
    {
        // Simple key term extraction
        var terms = text.Split(new[] { ' ', '.', ',', ';', ':', '(', ')', '[', ']', '{', '}', '\\', '/', '-', '_' },
                              StringSplitOptions.RemoveEmptyEntries)
                       .Select(t => t.Trim())
                       .Where(t => t.Length > 3 && !string.IsNullOrWhiteSpace(t))
                       .Distinct(StringComparer.OrdinalIgnoreCase)
                       .ToArray();

        return terms;
    }

    private List<Citation> CreateCitationsFromSources(List<SearchResult> sources, string claim)
    {
        var citations = new List<Citation>();
        
        foreach (var source in sources)
        {
            var citation = new Citation
            {
                SourceType = MapAgentTypeToCitationSourceType(source.Source.AgentType),
                SourceName = source.Source.SourceName,
                SourceUrl = source.Source.SourceUrl,
                ConfidenceScore = source.RelevanceScore,
                Verified = source.RelevanceScore >= 0.8f, // High confidence = verified
                VerificationMethod = source.RelevanceScore >= 0.8f ? "High relevance score" : "Content matching",
                VerifiedAt = DateTime.UtcNow,
                Metadata = new Dictionary<string, object>
                {
                    { "MatchedClaim", claim },
                    { "RelevanceScore", source.RelevanceScore },
                    { "ContentPreview", source.Content.Length > 100 ? source.Content[..100] + "…" : source.Content }
                }
            };

            // Add source-specific locator
            citation.Locator = CreateLocatorForSource(source);
            
            citations.Add(citation);
        }

        return citations;
    }

    private CitationSourceType MapAgentTypeToCitationSourceType(SearchAgentType agentType)
    {
        return agentType switch
        {
            SearchAgentType.VectorSearch => CitationSourceType.Dataset,
            SearchAgentType.WebSearch => CitationSourceType.Website,
            SearchAgentType.PDFSearch => CitationSourceType.ManualPdf,
            _ => CitationSourceType.Dataset
        };
    }

    private object CreateLocatorForSource(SearchResult source)
    {
        return source.Source.AgentType switch
        {
            SearchAgentType.VectorSearch => new DatasetCitationLocator
            {
                DatasetName = source.Source.SourceName,
                RecordId = source.Id,
                FieldName = "Content",
                DataSourceUrl = source.Source.SourceUrl,
                RetrievalTimestamp = source.GeneratedAt
            },
            
            SearchAgentType.WebSearch => new WebsiteCitationLocator
            {
                Url = source.Source.SourceUrl,
                Title = source.Source.SourceName,
                AccessedDate = source.GeneratedAt,
                TrustTier = WebsiteTrustTier.Standard
            },
            
            SearchAgentType.PDFSearch => new ManualPdfCitationLocator
            {
                DocumentId = source.Source.DocumentId,
                Title = source.Source.SourceName,
                PageNumber = 1, // Default, could be extracted from metadata
                SourceUrl = source.Source.SourceUrl,
                PublicationDate = source.Source.LastUpdated
            },
            
            _ => new DatasetCitationLocator
            {
                DatasetName = source.Source.SourceName,
                RecordId = source.Id,
                DataSourceUrl = source.Source.SourceUrl,
                RetrievalTimestamp = source.GeneratedAt
            }
        };
    }

    private async Task<string> GenerateCitedAnswerAsync(string originalAnswer, string[] claims, Dictionary<string, List<Citation>> citationMap)
    {
        try
        {
            // Build citation markers for each claim
            var citationMarkers = new Dictionary<string, string>();
            
            for (int i = 0; i < claims.Length; i++)
            {
                var claim = claims[i];
                var citations = citationMap[claim];
                
                if (citations.Any())
                {
                    var citationIds = string.Join(",", citations.Select((c, idx) => $"[{i + 1}-{idx + 1}]"));
                    citationMarkers[claim] = citationIds;
                }
                else
                {
                    // No citation available - qualify the statement
                    citationMarkers[claim] = "[Note: Could not verify this information]";
                }
            }

            // Generate final answer with citations
            var finalAnswer = originalAnswer;
            
            foreach (var claim in claims)
            {
                if (citationMarkers.TryGetValue(claim, out var marker))
                {
                    // Add citation marker after the claim
                    finalAnswer = finalAnswer.Replace(claim, $"{claim} {marker}");
                }
            }

            // Add citation references at the end
            finalAnswer += "\n\n### Sources and Citations\n";
            
            for (int i = 0; i < claims.Length; i++)
            {
                var claim = claims[i];
                var citations = citationMap[claim];
                
                if (citations.Any())
                {
                    finalAnswer += $"\n**Claim {i + 1}**: {claim}\n";
                    
                    for (int j = 0; j < citations.Count; j++)
                    {
                        var citation = citations[j];
                        finalAnswer += $"  - [{i + 1}-{j + 1}] {citation.SourceName}";
                        
                        if (!string.IsNullOrWhiteSpace(citation.SourceUrl))
                        {
                            finalAnswer += $" ([URL]({citation.SourceUrl}))";
                        }
                        
                        if (citation.Verified)
                        {
                            finalAnswer += " ✓ Verified";
                        }
                        else
                        {
                            finalAnswer += " ⚠ Requires verification";
                        }
                        
                        finalAnswer += "\n";
                    }
                }
            }

            return finalAnswer;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate cited answer");
            return originalAnswer; // Return original if citation generation fails
        }
    }

    private (bool IsPolicyCompliant, List<ClaimCitationIssue> Issues) EnforceClaimCitationPolicy(
        string answer, string[] claims, Dictionary<string, List<Citation>> citationMap)
    {
        var issues = new List<ClaimCitationIssue>();

        foreach (var claim in claims)
        {
            var citations = citationMap[claim];

            if (citations.Count == 0)
            {
                // No citations found for this claim
                if (!IsQualifiedClaim(claim) && !IsCommonKnowledge(claim))
                {
                    issues.Add(new ClaimCitationIssue
                    {
                        Claim = claim,
                        IssueType = ClaimCitationIssueType.MissingCitation,
                        Suggestion = "Add citation, qualify the statement, or omit if unverifiable"
                    });
                }
            }
            else
            {
                // Check citation quality
                var hasHighQualityCitation = citations.Any(c => c.Verified || c.ConfidenceScore >= 0.8f);
                
                if (!hasHighQualityCitation)
                {
                    issues.Add(new ClaimCitationIssue
                    {
                        Claim = claim,
                        IssueType = ClaimCitationIssueType.LowQualityCitation,
                        Suggestion = "Add verification or qualify the statement"
                    });
                }
            }
        }

        return (issues.Count == 0, issues);
    }

    private string ApplyClaimCitationPolicyCorrections(string originalAnswer, List<ClaimCitationIssue> issues)
    {
        var correctedAnswer = originalAnswer;

        foreach (var issue in issues)
        {
            switch (issue.IssueType)
            {
                case ClaimCitationIssueType.MissingCitation:
                    // Qualify the claim
                    if (correctedAnswer.Contains(issue.Claim))
                    {
                        var qualifiedClaim = "[Note: Could not verify] " + issue.Claim;
                        correctedAnswer = correctedAnswer.Replace(issue.Claim, qualifiedClaim);
                    }
                    break;

                case ClaimCitationIssueType.LowQualityCitation:
                    // Add qualification to low-quality citations
                    if (correctedAnswer.Contains(issue.Claim))
                    {
                        var qualifiedClaim = issue.Claim + " [Note: Requires verification]";
                        correctedAnswer = correctedAnswer.Replace(issue.Claim, qualifiedClaim);
                    }
                    break;
            }
        }

        return correctedAnswer;
    }

    private bool IsQualifiedClaim(string claim)
    {
        var qualifyingTerms = new[]
        {
            "may", "might", "could", "possibly", "potentially", "likely", "probably",
            "often", "sometimes", "typically", "generally", "usually", "can", "tend to"
        };

        return claim.ContainsAny(qualifyingTerms);
    }

    private bool IsCommonKnowledge(string claim)
    {
        // Simple common knowledge detection
        var commonKnowledgePatterns = new[]
        {
            "motorcycle", "vehicle", "engine", "wheels", "two-wheeled", "transportation",
            "ride", "driver", "passenger", "road", "street", "speed", "power"
        };

        var words = claim.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                         .Select(w => w.ToLower().Trim('.', ',', ';', ':', '!', '?'))
                         .Where(w => w.Length > 2)
                         .ToArray();

        if (words.Length == 0) return false;

        var commonWordCount = words.Count(w => commonKnowledgePatterns.Contains(w));
        return commonWordCount >= words.Length * 0.7; // 70% common words
    }

    private string GenerateNoResultsResponseWithRefinementSuggestions(string originalQuery)
    {
        // Analyze the query to provide specific refinement suggestions
        var queryAnalysis = AnalyzeQueryForRefinementSuggestions(originalQuery);
        
        var response = $"""
# No Results Found

I couldn't find any information matching your query: "{originalQuery}"

## Possible Reasons:
- The query may be too broad or too specific
- The information may not be available in our current data sources
- There might be a typo or unclear terminology

## Suggestions to Improve Your Search:

{queryAnalysis.Suggestions}

## Example Refined Queries:
{queryAnalysis.ExampleQueries}

## Additional Tips:
- Try using more specific motorcycle model names (e.g., "Honda CBR1000RR 2023 specifications")
- Include specific aspects you're interested in (e.g., "engine horsepower", "maintenance schedule", "top speed")
- Use technical terms when appropriate (e.g., "displacement", "torque curve", "ABS system")
- If looking for comparisons, specify what to compare (e.g., "Honda CBR1000RR vs Yamaha YZF-R1 performance")

If you believe this information should be available, please try rephrasing your query or contact support for assistance.
""";
        
        return response;
    }

    private QueryRefinementAnalysis AnalyzeQueryForRefinementSuggestions(string query)
    {
        var analysis = new QueryRefinementAnalysis
        {
            OriginalQuery = query,
            Suggestions = new List<string>(),
            ExampleQueries = new List<string>()
        };

        // Analyze query length
        if (query.Length < 10)
        {
            analysis.Suggestions.Add("✅ **Be more specific**: Your query is quite short. Add more details about what you're looking for.");
        }
        else if (query.Length > 100)
        {
            analysis.Suggestions.Add("✅ **Be more concise**: Your query is quite long. Try to focus on the key information you need.");
        }

        // Check for specific motorcycle terms
        var motorcycleTerms = new[] { "motorcycle", "bike", "specs", "specifications", "manual", "guide", "review", "comparison" };
        if (!query.ContainsAny(motorcycleTerms))
        {
            analysis.Suggestions.Add("✅ **Add context**: Include terms like 'motorcycle', 'specs', 'manual', or 'review' to help focus the search.");
        }

        // Check for brand/model names
        var commonBrands = new[] { "Honda", "Yamaha", "Kawasaki", "Suzuki", "Ducati", "BMW", "Harley", "Triumph" };
        var hasBrand = query.ContainsAny(commonBrands);

        // Check for model indicators
        var modelIndicators = new[] { "CBR", "R1", "ZX", "GSX", "Panigale", "S1000", "Street", "Ninja" };
        var hasModelIndicator = query.ContainsAny(modelIndicators);

        if (!hasBrand && !hasModelIndicator)
        {
            analysis.Suggestions.Add("✅ **Specify brands/models**: Include specific motorcycle brands (Honda, Yamaha) or model names (CBR1000RR, YZF-R1) for better results.");
        }

        // Check for technical terms
        var technicalTerms = new[] { "engine", "horsepower", "torque", "displacement", "suspension", "brakes", "ABS", "traction control" };
        if (!query.ContainsAny(technicalTerms))
        {
            analysis.Suggestions.Add("✅ **Use technical terms**: Include specific aspects you're interested in (engine, horsepower, suspension, ABS, etc.).");
        }

        // Generate example queries based on analysis
        if (hasBrand || hasModelIndicator)
        {
            analysis.ExampleQueries.Add($"{ExtractMainSubject(query)} specifications and performance data");
            analysis.ExampleQueries.Add($"{ExtractMainSubject(query)} engine horsepower and torque curve");
            analysis.ExampleQueries.Add($"{ExtractMainSubject(query)} maintenance schedule and service intervals");
        }
        else
        {
            analysis.ExampleQueries.Add("Honda CBR1000RR 2023 specifications");
            analysis.ExampleQueries.Add("Yamaha YZF-R1 vs Kawasaki Ninja ZX-10R comparison");
            analysis.ExampleQueries.Add("Ducati Panigale V4 maintenance guide");
        }

        // Add year/version suggestion if not present
        if (!System.Text.RegularExpressions.Regex.IsMatch(query, @"\d{4}")) // No 4-digit year
        {
            analysis.Suggestions.Add("✅ **Include year/version**: Add the model year (e.g., '2023') for more accurate specifications.");
        }

        if (analysis.Suggestions.Count == 0)
        {
            analysis.Suggestions.Add("✅ **Try different wording**: Rephrase your query using alternative terms or structure.");
            analysis.Suggestions.Add("✅ **Check spelling**: Ensure all terms are spelled correctly, especially model names.");
        }

        return analysis;
    }

    private string ExtractMainSubject(string query)
    {
        // Simple extraction of main subject (brand/model)
        var commonBrands = new[] { "Honda", "Yamaha", "Kawasaki", "Suzuki", "Ducati", "BMW", "Harley", "Triumph" };
        var modelIndicators = new[] { "CBR", "R1", "ZX", "GSX", "Panigale", "S1000", "Street", "Ninja" };

        foreach (var brand in commonBrands)
        {
            if (query.Contains(brand))
                return brand;
        }

        foreach (var model in modelIndicators)
        {
            if (query.Contains(model))
                return model;
        }

        // Return first significant word
        var words = query.Split(new[] { " " }, StringSplitOptions.RemoveEmptyEntries);
        return words.Length > 0 ? words[0] : "motorcycle";
    }

    private SearchResult[] EnsureAllResultsHaveCitations(SearchResult[] results)
    {
        foreach (var result in results)
        {
            if (result.Source.Citation == null)
            {
                // Create a basic citation if none exists
                result.Source.Citation = new Citation
                {
                    SourceType = MapAgentTypeToCitationSourceType(result.Source.AgentType),
                    SourceName = result.Source.SourceName,
                    SourceUrl = result.Source.SourceUrl,
                    ConfidenceScore = result.RelevanceScore,
                    Verified = result.RelevanceScore >= 0.8f,
                    VerificationMethod = "Automated citation generation",
                    VerifiedAt = DateTime.UtcNow,
                    Locator = CreateLocatorForSource(result)
                };
            }
        }
        
        return results;
    }

    private decimal CalculateEstimatedCost(SearchResult[] results, string response)
    {
        // Simple cost estimation based on tokens and operations
        var inputTokens = results.Sum(r => r.Content?.Length ?? 0) / 4; // Rough token estimation
        var outputTokens = response.Length / 4;

        // Estimated costs (these would be configured based on actual Azure pricing)
        var inputCostPer1K = 0.0015m; // $0.0015 per 1K input tokens
        var outputCostPer1K = 0.002m;  // $0.002 per 1K output tokens

        var inputCost = (inputTokens / 1000m) * inputCostPer1K;
        var outputCost = (outputTokens / 1000m) * outputCostPer1K;

        return inputCost + outputCost;
    }
}
