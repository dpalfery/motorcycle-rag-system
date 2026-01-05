using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Application.Caching;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Application.Services;

/// <summary>
/// Claim citation issue types
/// </summary>
public enum ClaimCitationIssueType {
    MissingCitation,
    LowQualityCitation,
    UnverifiableClaim
}

/// <summary>
/// Claim citation issue
/// </summary>
public class ClaimCitationIssue {
    public string Claim { get; set; } = string.Empty;
    public ClaimCitationIssueType IssueType { get; set; }
    public string Suggestion { get; set; } = string.Empty;
}

/// <summary>
/// Query refinement analysis for no-results responses
/// </summary>
public class QueryRefinementAnalysis {
    public string OriginalQuery { get; set; } = string.Empty;
    public IReadOnlyList<string> Suggestions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ExampleQueries { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Main service coordinating the complete retrieval-augmented generation (RAG) pipeline for motorcycle queries.
/// Enhanced with caching and performance optimizations.
/// </summary>
public sealed class MotorcycleRagService : IMotorcycleRagService {
    private static readonly string[] JsonArraySeparators = { "\",\"" };
    private static readonly char[] SentenceSeparators = { '.', '!', '?' };
    private static readonly string[] FactIndicators = { " is ", " has ", " are ", " was ", " were " };
    private static readonly char[] TrimChars = { '[', ']', ' ', '\\', '"' };
    private static readonly string[] CommonKnowledgePatterns = {
        "MOTORCYCLE", "VEHICLE", "ENGINE", "WHEELS", "TWO-WHEELED", "TRANSPORTATION",
        "RIDE", "DRIVER", "PASSENGER", "ROAD", "STREET", "SPEED", "POWER"
    };
    private static readonly char[] WordSplitSeparators = { ' ' };
    private static readonly char[] KeyTermSeparators = { ' ', '.', ',', ';', ':', '(', ')', '[', ']', '{', '}', '\\', '/', '-', '_' };
    private static readonly string[] CommonBrands = { "Honda", "Yamaha", "Kawasaki", "Suzuki", "Ducati", "BMW", "Harley", "Triumph" };
    private static readonly string[] ModelIndicators = { "CBR", "R1", "ZX", "GSX", "Panigale", "S1000", "Street", "Ninja" };
    private static readonly string[] MotorcycleTerms = { "motorcycle", "bike", "specs", "specifications", "manual", "guide", "review", "comparison" };
    private static readonly string[] TechnicalTerms = { "engine", "horsepower", "torque", "displacement", "suspension", "brakes", "ABS", "traction control" };
    private static readonly char[] CleanTrimChars = { '.', ',', ';', ':', '!', '?' };
    private readonly IAgentOrchestrator _orchestrator;
    private readonly ILogger<MotorcycleRagService> _logger;
    private readonly ITelemetryService _telemetryService;
    private readonly IQueryCacheService _cacheService;
    private readonly CacheConfiguration _cacheConfig;
    private readonly IAzureOpenAIClient _openAIClient;
    private const decimal InputCostPer1K = 0.0015m; 
    private const decimal OutputCostPer1K = 0.002m;

    public MotorcycleRagService(
        IAgentOrchestrator orchestrator,
        ILogger<MotorcycleRagService> logger,
        ITelemetryService telemetryService,
        IQueryCacheService cacheService,
        IOptions<CacheConfiguration> cacheConfig,
        IAzureOpenAIClient openAIClient) {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _telemetryService = telemetryService ?? throw new ArgumentNullException(nameof(telemetryService));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        _cacheConfig = cacheConfig?.Value ?? throw new ArgumentNullException(nameof(cacheConfig));
        _openAIClient = openAIClient ?? throw new ArgumentNullException(nameof(openAIClient));
    }

    /// <inheritdoc />
    public async Task<MotorcycleQueryResponse> SearchAsync(MotorcycleQueryRequest request) {
        return await QueryAsync(request);
    }

    /// <inheritdoc />
    public async Task<MotorcycleQueryResponse> QueryAsync(MotorcycleQueryRequest request) {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Query))
            throw new ArgumentException("Query cannot be null or empty", nameof(request));

        var queryId = Guid.NewGuid().ToString("N");
        _logger.LogInformation("[{QueryId}] Processing motorcycle RAG query: {Query}", queryId, request.Query);

        var stopwatch = Stopwatch.StartNew();

        // 1. Check cache first
        var cachedResponse = await TryGetCachedResponseAsync(request, queryId);
        if (cachedResponse != null) {
            stopwatch.Stop();
            _logger.LogInformation("[{QueryId}] Cache hit for query. Duration: {Duration}ms", queryId, stopwatch.ElapsedMilliseconds);
            return cachedResponse;
        }

        // 2. Execute orchestrated search
        var results = await ExecuteSearchAsync(request);

        // 3. Generate initial response
        var answer = await _orchestrator.GenerateResponseAsync(results, request.Query);

        // 4. Handle no-results
        if (results.Length == 0 || string.IsNullOrWhiteSpace(answer)) {
            answer = GenerateNoResultsResponseWithRefinementSuggestions(request.Query);
        }

        stopwatch.Stop();
        return await FinalizeResponseAsync(request, queryId, results, answer ?? string.Empty, stopwatch.Elapsed);
    }

    private async Task<MotorcycleQueryResponse?> TryGetCachedResponseAsync(MotorcycleQueryRequest request, string queryId) {
        if (!_cacheConfig.EnableCaching) return null;

        var cacheKey = _cacheService.GenerateCacheKey(request);
        var cachedResponse = await _cacheService.GetAsync(cacheKey);

        if (cachedResponse == null) return null;

        // Update cached response with new query ID and timestamp
        cachedResponse.QueryId = queryId;
        cachedResponse.GeneratedAt = DateTime.UtcNow;

        if (cachedResponse.Metrics != null) {
            cachedResponse.Metrics.CacheHit = true;
        }

        _telemetryService.TrackQuery(queryId, request.Query, TimeSpan.Zero,
            cachedResponse.Sources?.Length ?? 0, cachedResponse.Metrics?.EstimatedCost ?? 0);

        return cachedResponse;
    }

    private async Task<SearchResult[]> ExecuteSearchAsync(MotorcycleQueryRequest request) {
        var context = new SearchContext {
            SessionId = request.Context?.SessionId ?? Guid.NewGuid().ToString(),
            Preferences = request.Preferences,
            QueryContext = request.Context ?? new QueryContext()
        };

        return await _orchestrator.ExecuteSequentialSearchAsync(request.Query, context) ?? Array.Empty<SearchResult>();
    }

    private async Task<MotorcycleQueryResponse> FinalizeResponseAsync(MotorcycleQueryRequest request, string queryId, SearchResult[] results, string answer, TimeSpan duration) {
        var estimatedCost = CalculateEstimatedCost(results, answer);

        var metrics = new QueryMetrics {
            ProcessingTimeMs = (int)duration.TotalMilliseconds,
            TotalDuration = duration,
            ResultsFound = results.Length,
            CacheHit = false,
            EstimatedCost = estimatedCost
        };

        // Extract claims and ensure citations
        var (finalAnswer, finalResults) = await ExtractClaimsAndEnsureCitationsAsync(answer, results, request.Query);

        // Analyze sources and inject limitations
        var limitationMessages = AnalyzeLimitationsAndCreateMessages(results, metrics, queryId);
        var responseWithLimitations = InjectLimitationMessages(finalAnswer, limitationMessages);

        var response = new MotorcycleQueryResponse {
            QueryId = queryId,
            Response = responseWithLimitations,
            Sources = finalResults,
            Metrics = metrics,
            GeneratedAt = DateTime.UtcNow
        };

        // Cache if appropriate
        if (_cacheConfig.EnableCaching && ShouldCacheResponse(response)) {
            var cacheKey = _cacheService.GenerateCacheKey(request);
            var expiration = DetermineCacheExpiration(response);
            await _cacheService.SetAsync(cacheKey, response, expiration);
        }

        _telemetryService.TrackQuery(queryId, request.Query, duration, results.Length, estimatedCost);
        return response;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> GetHealthAsync() {
        var result = new HealthCheckResult {
            IsHealthy = true,
            Status = "OK",
            Details =
            {
                ["Timestamp"] = DateTime.UtcNow
            }
        };

        // Add cache statistics to health check
        if (_cacheConfig.EnableCaching) {
            try {
                var cacheStats = await _cacheService.GetStatisticsAsync();
                result.Details["Cache.HitRatio"] = $"{cacheStats.HitRatio:P2}";
                result.Details["Cache.TotalEntries"] = cacheStats.TotalEntries.ToString();
                result.Details["Cache.MemoryUsage"] = $"{cacheStats.TotalMemoryUsage / 1024 / 1024:F1}MB";
            }
            catch (Exception ex) {
                _logger.LogWarning(ex, "Failed to get cache statistics for health check");
                result.Details["Cache.Status"] = "Error";
            }
        }

        return result;
    }

    private bool ShouldCacheResponse(MotorcycleQueryResponse response) {
        // Cache responses that have good results and reasonable processing time
        return response.Sources?.Length > 0 &&
               response.Metrics?.ProcessingTimeMs < 30000 && // Less than 30 seconds
               !string.IsNullOrWhiteSpace(response.Response);
    }

    private TimeSpan DetermineCacheExpiration(MotorcycleQueryResponse response) {
        // Use longer expiration for high-quality responses
        if (response.Sources?.Length > 3 && response.Metrics?.ProcessingTimeMs < 5000) {
            return _cacheConfig.LongTermExpiration;
        }

        return _cacheConfig.DefaultExpiration;
    }

    private async Task<(string FinalAnswer, SearchResult[] FinalResults)> ExtractClaimsAndEnsureCitationsAsync(
        string originalAnswer, SearchResult[] originalResults, string query) {
        try {
            _logger.LogInformation("Extracting claims and ensuring citations for query: {Query}", query);

            // 1. Identify factual claims in the answer
            var claims = await IdentifyFactualClaimsAsync(originalAnswer);

            if (claims == null || claims.Length == 0) {
                _logger.LogDebug("No factual claims identified in answer");
                return (originalAnswer, originalResults);
            }

            _logger.LogDebug("Identified {ClaimCount} factual claims: {Claims}", claims.Length, string.Join(", ", claims));

            // 2. Match claims to evidence sources and create citations
            var resultsWithCitations = new List<SearchResult>();
            var claimEvidences = new Dictionary<string, ClaimEvidence>(); // claim -> evidence

            foreach (var claim in claims) {
                var matchingSources = FindMatchingSourcesForClaim(claim, originalResults);
                var evidence = new ClaimEvidence();

                if (matchingSources.Any()) {
                    evidence.Citations.AddRange(CreateCitationsFromSources(matchingSources, claim));
                    
                    // Add to results with enhanced citations
                    foreach (var source in matchingSources) {
                        if (resultsWithCitations.All(r => r.Id != source.Id)) {
                            resultsWithCitations.Add(source);
                        }
                    }
                }
                else {
                    _logger.LogWarning("No evidence found for claim: {Claim}", claim);
                }
                claimEvidences[claim] = evidence;
            }

            // 3. Generate final answer with proper citations
            var finalAnswer = await GenerateCitedAnswerAsync(originalAnswer, claims, claimEvidences);

            // 4. Ensure all results have proper citations
            var finalResults = EnsureAllResultsHaveCitations(resultsWithCitations.ToArray());

            _logger.LogInformation("Claim extraction and citation completed. Final answer length: {Length}, results with citations: {Count}",
                finalAnswer.Length, finalResults.Count(r => r.Source.Citation != null));

            // Enforce: every factual claim has citation or is qualified/omitted
            var (isCompliant, issues) = EnforceClaimCitationPolicy(claims, claimEvidences);
            if (!isCompliant) {
                _logger.LogWarning("Claim-citation policy enforcement: {Issues} issues found", issues.Count);

                // Apply corrections to make the response policy-compliant
                finalAnswer = ApplyClaimCitationPolicyCorrections(finalAnswer, issues);

                _logger.LogInformation("Applied claim-citation policy corrections to ensure compliance");
            }

            return (finalAnswer, finalResults);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Failed to extract claims and ensure citations");
            // Return original data on failure
            return (originalAnswer, originalResults);
        }
    }

    private async Task<string[]> IdentifyFactualClaimsAsync(string answer) {
        try {
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
            if (claimsJson.StartsWith('[') && claimsJson.EndsWith(']')) {
                var claims = claimsJson
                    .Trim(TrimChars)
                    .Split(JsonArraySeparators, StringSplitOptions.RemoveEmptyEntries)
                    .Select(c => c.Trim('"', ' ', '\\'))
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .ToArray();

                return claims;
            }

            // Fallback: extract sentences that look like facts
            return ExtractFactLikeSentences(answer);
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "Failed to identify factual claims using AI, falling back to simple extraction");
            return ExtractFactLikeSentences(answer);
        }
    }

    private string[] ExtractFactLikeSentences(string answer) {
        // Simple fallback: extract sentences that contain numbers, specific terms, etc.
        return answer.Split(SentenceSeparators, StringSplitOptions.RemoveEmptyEntries)
                       .Select(s => s.Trim())
                       .Where(s => !string.IsNullOrWhiteSpace(s))
                       .Where(s => FactIndicators.Any(indicator => s.Contains(indicator, StringComparison.OrdinalIgnoreCase)) || s.Any(char.IsDigit))
                       .ToArray();
    }

    private List<SearchResult> FindMatchingSourcesForClaim(string claim, SearchResult[] results) {
        // Simple text matching for now (could be enhanced with semantic search)
        return results.Where(result => 
            result.Content.Contains(claim, StringComparison.OrdinalIgnoreCase) ||
            ExtractKeyTerms(result.Content).Intersect(ExtractKeyTerms(claim), StringComparer.OrdinalIgnoreCase).Any()
        ).ToList();
    }


    private string[] ExtractKeyTerms(string text) {
        // Simple key term extraction
        var terms = text.Split(KeyTermSeparators, StringSplitOptions.RemoveEmptyEntries)
                       .Select(t => t.Trim())
                       .Where(t => t.Length > 3 && !string.IsNullOrWhiteSpace(t))
                       .Distinct(StringComparer.OrdinalIgnoreCase)
                       .ToArray();

        return terms;
    }

    private List<Citation> CreateCitationsFromSources(List<SearchResult> sources, string claim) {
        var citations = new List<Citation>();

        foreach (var source in sources) {
            var citation = new Citation {
                SourceType = MapAgentTypeToCitationSourceType(source.Source.AgentType),
                SourceName = source.Source.SourceName,
                SourceUrl = source.Source.SourceUrl?.ToString(),
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

    private CitationSourceType MapAgentTypeToCitationSourceType(SearchAgentType agentType) {
        return agentType switch {
            SearchAgentType.VectorSearch => CitationSourceType.Dataset,
            SearchAgentType.WebSearch => CitationSourceType.Website,
            SearchAgentType.PDFSearch => CitationSourceType.ManualPdf,
            _ => CitationSourceType.Dataset
        };
    }

    private object CreateLocatorForSource(SearchResult source) {
        return source.Source.AgentType switch {
            SearchAgentType.VectorSearch => new DatasetCitationLocator {
                DatasetName = source.Source.SourceName,
                RecordId = source.Id,
                FieldName = "Content",
                DataSourceUrl = source.Source.SourceUrl?.ToString() ?? string.Empty,
                RetrievalTimestamp = source.GeneratedAt
            },

            SearchAgentType.WebSearch => new WebsiteCitationLocator {
                Url = source.Source.SourceUrl?.ToString() ?? string.Empty,
                Title = source.Source.SourceName,
                AccessedDate = source.GeneratedAt,
                TrustTier = WebsiteTrustTier.Standard
            },

            SearchAgentType.PDFSearch => CreateManualPdfLocator(source),

            _ => new DatasetCitationLocator {
                DatasetName = source.Source.SourceName,
                RecordId = source.Id,
                DataSourceUrl = source.Source.SourceUrl?.ToString() ?? string.Empty,
                RetrievalTimestamp = source.GeneratedAt
            }
        };
    }

    /// <summary>
    /// Creates a ManualPdfCitationLocator from search result metadata
    /// Maps locator fields from T055 indexed chunk metadata
    /// </summary>
    private ManualPdfCitationLocator CreateManualPdfLocator(SearchResult source) {
        var locator = new ManualPdfCitationLocator {
            DocumentId = source.Source.DocumentId,
            Title = source.Source.SourceName,
            PageNumber = 1,
            SourceUrl = source.Source.SourceUrl?.ToString() ?? string.Empty,
            PublicationDate = source.Source.LastUpdated
        };

        if (source.Metadata == null || source.Metadata.Count == 0) return locator;

        MapMetadataToLocator(source.Metadata, locator);
        MapLegacyLocator(source.Metadata, locator);

        return locator;
    }

    private static void MapMetadataToLocator(IReadOnlyDictionary<string, object> metadata, ManualPdfCitationLocator locator) {
        if (metadata.TryGetValue("PageNumber", out var pn) && pn is int pageNum && pageNum > 0)
            locator.PageNumber = pageNum;
        
        if (metadata.TryGetValue("PageRange", out var pr) && pr is string pageRange)
            locator.PageRange = pageRange;
        
        if (metadata.TryGetValue("PrimarySection", out var ps) && ps is string primarySection)
            locator.PrimarySection = primarySection;
        
        if (metadata.TryGetValue("SectionLevel", out var sl) && sl is int sectionLevel)
            locator.SectionLevel = sectionLevel;
        
        if (metadata.TryGetValue("SectionHeadings", out var sh) && sh is string[] sectionHeadings && sectionHeadings.Length > 0) {
            locator.SectionHeadings = sectionHeadings;
            locator.PrimarySection = sectionHeadings[0];
        }
        
        if (metadata.TryGetValue("TableCaption", out var tc) && tc is string tableCaption)
            locator.TableCaption = tableCaption;
        
        if (metadata.TryGetValue("ChunkIndex", out var ci) && ci is int chunkIndex)
            locator.ChunkIndex = chunkIndex;
    }

    private static void MapLegacyLocator(IReadOnlyDictionary<string, object> metadata, ManualPdfCitationLocator locator) {
        if (metadata.TryGetValue("AdditionalProperties", out var ap) && ap is Dictionary<string, object> props &&
            props.TryGetValue("Locator", out var legacy) && legacy is string legacyLoc && !string.IsNullOrWhiteSpace(legacyLoc)) {
            locator.Section = legacyLoc;
        }
    }

    private async Task<string> GenerateCitedAnswerAsync(string originalAnswer, string[] claims, Dictionary<string, ClaimEvidence> claimEvidences) {
        try {
            // Build citation markers for each claim
            var citationMarkers = new Dictionary<string, string>();

            for (int i = 0; i < claims.Length; i++) {
                var claim = claims[i];
                var citations = claimEvidences[claim].Citations;

                citationMarkers[claim] = citations.Count != 0
                    ? string.Join(",", citations.Select((_, idx) => $"[{i + 1}-{idx + 1}]"))
                    : "[Note: Could not verify this information]";
            }

            // Generate final answer with citations
            var finalAnswer = originalAnswer;

            foreach (var claim in claims) {
                if (citationMarkers.TryGetValue(claim, out var marker)) {
                    // Add citation marker after the claim
                    finalAnswer = finalAnswer.Replace(claim, $"{claim} {marker}");
                }
            }

            // Add citation references at the end
            finalAnswer += "\n\n### Sources and Citations\n";

            for (int i = 0; i < claims.Length; i++) {
                var claim = claims[i];
                var citations = claimEvidences[claim].Citations;

                if (citations.Count == 0) continue;

                finalAnswer = string.Concat(finalAnswer, "\n**Claim ", (i + 1), "**: ", claim, "\n");

                for (int j = 0; j < citations.Count; j++) {
                    var citation = citations[j];
                    finalAnswer = string.Concat(finalAnswer, "  - [", (i + 1), "-", (j + 1), "] ", citation.SourceName);

                    if (citation.SourceUrl != null) {
                        finalAnswer = string.Concat(finalAnswer, " ([URL](", citation.SourceUrl, "))");
                    }

                    finalAnswer = string.Concat(finalAnswer, (citation.Verified ? " ✓ Verified" : " ⚠ Requires verification"), "\n");
                }
            }

            return finalAnswer;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Failed to generate cited answer");
            return originalAnswer; // Return original if citation generation fails
        }
    }

    private (bool IsPolicyCompliant, List<ClaimCitationIssue> Issues) EnforceClaimCitationPolicy(
        string[] claims, Dictionary<string, ClaimEvidence> claimEvidences) {
        var issues = new List<ClaimCitationIssue>();

        foreach (var claim in claims) {
            var citations = claimEvidences[claim].Citations;

            if (citations.Count == 0) {
                // No citations found for this claim
                if (!IsQualifiedClaim(claim) && !IsCommonKnowledge(claim)) {
                    issues.Add(new ClaimCitationIssue {
                        Claim = claim,
                        IssueType = ClaimCitationIssueType.MissingCitation,
                        Suggestion = "Add citation, qualify the statement, or omit if unverifiable"
                    });
                }
            }
            else {
                // Check citation quality
                var hasHighQualityCitation = citations.Any(c => c.Verified || c.ConfidenceScore >= 0.8f);

                if (!hasHighQualityCitation) {
                    issues.Add(new ClaimCitationIssue {
                        Claim = claim,
                        IssueType = ClaimCitationIssueType.LowQualityCitation,
                        Suggestion = "Add verification or qualify the statement"
                    });
                }
            }
        }

        return (issues.Count == 0, issues);
    }

    private string ApplyClaimCitationPolicyCorrections(string originalAnswer, List<ClaimCitationIssue> issues) {
        var correctedAnswer = originalAnswer;

        foreach (var issue in issues) {
            if (issue.IssueType == ClaimCitationIssueType.MissingCitation && correctedAnswer.Contains(issue.Claim)) {
                // Qualify the claim
                var qualifiedClaim = string.Concat("[Note: Could not verify] ", issue.Claim);
                correctedAnswer = correctedAnswer.Replace(issue.Claim, qualifiedClaim);
            }
            else if (issue.IssueType == ClaimCitationIssueType.LowQualityCitation && correctedAnswer.Contains(issue.Claim)) {
                // Add qualification to low-quality citations
                var qualifiedClaim = string.Concat(issue.Claim, " [Note: Requires verification]");
                correctedAnswer = correctedAnswer.Replace(issue.Claim, qualifiedClaim);
            }
        }

        return correctedAnswer;
    }

    private bool IsQualifiedClaim(string claim) {
        return QualifyingTerms.Any(term => claim.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static readonly string[] QualifyingTerms = {
        "may", "might", "could", "possibly", "potentially", "likely", "probably",
        "often", "sometimes", "typically", "generally", "usually", "can", "tend to"
    };

    private bool IsCommonKnowledge(string claim) {
        // Simple common knowledge detection
        var words = claim.Split(WordSplitSeparators, StringSplitOptions.RemoveEmptyEntries)
                         .Select(w => w.ToUpperInvariant().Trim(CleanTrimChars))
                         .Where(w => w.Length > 2)
                         .ToArray();

        if (words.Length == 0) return false;

        var commonWordCount = words.Count(w => CommonKnowledgePatterns.Contains(w));
        return commonWordCount >= words.Length * 0.7; // 70% common words
    }

    private string GenerateNoResultsResponseWithRefinementSuggestions(string originalQuery) {
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

    private QueryRefinementAnalysis AnalyzeQueryForRefinementSuggestions(string query) {
        var suggestions = new List<string>();
        var exampleQueries = new List<string>();

        // Analyze query length
        if (query.Length < 10) {
            suggestions.Add("✅ **Be more specific**: Your query is quite short. Add more details about what you're looking for.");
        }
        else if (query.Length > 100) {
            suggestions.Add("✅ **Be more concise**: Your query is quite long. Try to focus on the key information you need.");
        }

        // Check for specific motorcycle terms
        if (!MotorcycleTerms.Any(term => query.Contains(term, StringComparison.OrdinalIgnoreCase))) {
            suggestions.Add("✅ **Add context**: Include terms like 'motorcycle', 'specs', 'manual', or 'review' to help focus the search.");
        }

        // Check for brand/model names
        var hasBrand = CommonBrands.Any(brand => query.Contains(brand, StringComparison.OrdinalIgnoreCase));

        // Check for model indicators
        var hasModelIndicator = ModelIndicators.Any(model => query.Contains(model, StringComparison.OrdinalIgnoreCase));

        if (!hasBrand && !hasModelIndicator) {
            suggestions.Add("✅ **Specify brands/models**: Include specific motorcycle brands (Honda, Yamaha) or model names (CBR1000RR, YZF-R1) for better results.");
        }

        // Check for technical terms
        if (!TechnicalTerms.Any(term => query.Contains(term, StringComparison.OrdinalIgnoreCase))) {
            suggestions.Add("✅ **Use technical terms**: Include specific aspects you're interested in (engine, horsepower, suspension, ABS, etc.).");
        }

        // Generate example queries based on analysis
        if (hasBrand || hasModelIndicator) {
            exampleQueries.Add($"{ExtractMainSubject(query)} specifications and performance data");
            exampleQueries.Add($"{ExtractMainSubject(query)} engine horsepower and torque curve");
            exampleQueries.Add($"{ExtractMainSubject(query)} maintenance schedule and service intervals");
        }
        else {
            exampleQueries.Add("Honda CBR1000RR 2023 specifications");
            exampleQueries.Add("Yamaha YZF-R1 vs Kawasaki Ninja ZX-10R comparison");
            exampleQueries.Add("Ducati Panigale V4 maintenance guide");
        }

        // Add year/version suggestion if not present
        if (!System.Text.RegularExpressions.Regex.IsMatch(query, @"\d{4}")) // No 4-digit year
        {
            suggestions.Add("✅ **Include year/version**: Add the model year (e.g., '2023') for more accurate specifications.");
        }

        if (suggestions.Count == 0) {
            suggestions.Add("✅ **Try different wording**: Rephrase your query using alternative terms or structure.");
            suggestions.Add("✅ **Check spelling**: Ensure all terms are spelled correctly, especially model names.");
        }

        return new QueryRefinementAnalysis {
            OriginalQuery = query,
            Suggestions = suggestions,
            ExampleQueries = exampleQueries
        };
    }

    private string ExtractMainSubject(string query) {
        // Simple extraction of main subject (brand/model)
        var brand = CommonBrands.FirstOrDefault(b => query.Contains(b, StringComparison.OrdinalIgnoreCase));
        if (brand != null) return brand;

        var model = ModelIndicators.FirstOrDefault(m => query.Contains(m, StringComparison.OrdinalIgnoreCase));
        if (model != null) return model;

        // Return first significant word
        var words = query.Split(WordSplitSeparators, StringSplitOptions.RemoveEmptyEntries);
        return words.Length > 0 ? words[0] : "motorcycle";
    }

    private SearchResult[] EnsureAllResultsHaveCitations(SearchResult[] results) {
        foreach (var result in results) {
            result.Source.Citation ??= new Citation {
                SourceType = MapAgentTypeToCitationSourceType(result.Source.AgentType),
                SourceName = result.Source.SourceName,
                SourceUrl = result.Source.SourceUrl?.ToString(),
                ConfidenceScore = result.RelevanceScore,
                Verified = result.RelevanceScore >= 0.8f,
                VerificationMethod = "Automated citation generation",
                VerifiedAt = DateTime.UtcNow,
                Locator = CreateLocatorForSource(result)
            };
        }

        return results;
    }

    private decimal CalculateEstimatedCost(SearchResult[] results, string response) {
        // Simple cost estimation based on tokens and operations
        var inputTokens = results.Sum(r => r.Content?.Length ?? 0) / 4; // Rough token estimation
        var outputTokens = response.Length / 4;

        var inputCost = (inputTokens / 1000m) * InputCostPer1K;
        var outputCost = (outputTokens / 1000m) * OutputCostPer1K;

        return inputCost + outputCost;
    }

    /// <summary>
    /// Analyzes search execution metadata to identify limitations and creates user-friendly messages.
    /// </summary>
    /// <remarks>
    /// This method examines the SearchPatternMetrics from execution to determine:
    /// - Whether all expected sources were searched
    /// - Whether any sources failed or returned no results
    /// - The overall quality and completeness of the response
    /// 
    /// Limitation messages are created to inform users about:
    /// - Partial source availability (some sources unavailable)
    /// - Complete failures (no results from any source)
    /// - Degraded service (partial sources only)
    /// </remarks>
    private List<string> AnalyzeLimitationsAndCreateMessages(SearchResult[] results, QueryMetrics metrics, string queryId) {
        var messages = new List<string>();

        try {
            // If we have no results at all, this is a special case handled elsewhere
            if (results == null || results.Length == 0) {
                _logger.LogWarning("[{QueryId}] No results found from any source", queryId);
                return messages; // Handled by GenerateNoResultsResponseWithRefinementSuggestions
            }

            // Check SearchPatternMetrics for source availability
            if (metrics?.SearchPattern != null) {
                var pattern = metrics.SearchPattern;
                var sourcesWithResults = 0;
                var sourcesExecuted = 0;
                var failedSources = new List<string>();

                AnalyzePatternResults(pattern, ref sourcesExecuted, ref sourcesWithResults, failedSources);

                // Generate limitation messages based on results
                if (sourcesExecuted > 0 && sourcesWithResults < sourcesExecuted && sourcesWithResults > 0) {
                    // Some sources failed but we have results from others
                    messages.Add($"⚠️ **Partial Results**: Some sources ({string.Join(", ", failedSources)}) are currently unavailable or returned no results. The information below may be limited.");
                    _logger.LogInformation("[{QueryId}] Partial source availability: {Available}/{Total} sources returned results", queryId, sourcesWithResults, sourcesExecuted);
                }
                else if (sourcesWithResults == 0 && sourcesExecuted > 0) {
                    // All sources failed
                    messages.Add($"❌ **Service Degradation**: We were unable to retrieve results from " +
                        $"{(sourcesExecuted == 1 ? "the" : "any of the")} " +
                        $"available source{(sourcesExecuted == 1 ? "" : "s")}. " +
                        "Try rephrasing your query or check back later.");
                    _logger.LogWarning("[{QueryId}] All sources failed: {FailedCount} sources attempted", queryId, sourcesExecuted);
                }
            }

            // Check for low result count (might indicate partial search)
            if (results.Length == 1) {
                messages.Add("ℹ️ **Limited Results**: Only one result was found. For more comprehensive information, try refining your query with additional details.");
                _logger.LogDebug("[{QueryId}] Very limited results: only {Count} result found", queryId, results.Length);
            }

            // Check for low relevance scores indicating poor matches
            if (results.All(r => r.RelevanceScore < 0.5f)) {
                messages.Add("⚠️ **Low Confidence**: The results found have low relevance scores. Consider rephrasing your question for better matches.");
                _logger.LogDebug("[{QueryId}] Low relevance scores detected (all < 0.5)", queryId);
            }

            _logger.LogDebug("[{QueryId}] Limitation analysis complete: {MessageCount} messages generated", queryId, messages.Count);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "[{QueryId}] Error analyzing limitations", queryId);
            // Continue without limitation messages on error
        }

        return messages;
    }

    /// <summary>
    /// Injects limitation messages into the response in a user-friendly format.
    /// </summary>
    /// <remarks>
    /// Messages are prepended to the response with clear visual separation and actionable guidance.
    /// The format is designed for both markdown rendering and plain text display.
    /// </remarks>
    private string InjectLimitationMessages(string originalResponse, List<string> limitationMessages) {
        if (limitationMessages == null || limitationMessages.Count == 0) {
            return originalResponse;
        }

        var messageText = string.Join("\n\n", limitationMessages);

        return $"""
{messageText}

---

{originalResponse}
""";
    }

    private static void AnalyzePatternResults(SearchPatternMetrics pattern, ref int executed, ref int withResults, List<string> failed) {
        if (pattern.VectorSearchExecuted) {
            executed++;
            if (pattern.VectorResultsFound > 0) withResults++;
            else failed.Add("vector search (indexed specifications)");
        }
        if (pattern.WebSearchExecuted) {
            executed++;
            if (pattern.WebResultsFound > 0) withResults++;
            else failed.Add("web search (trusted sources)");
        }
        if (pattern.PDFSearchExecuted) {
            executed++;
            if (pattern.PDFResultsFound > 0) withResults++;
            else failed.Add("PDF manual search");
        }
    }

    private class ClaimEvidence {
        public List<Citation> Citations { get; } = new();
    }
}
