using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Application.Caching;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Application.Services.Citations;
using MotorcycleRAG.Application.Services.QueryProcessing;
using MotorcycleRAG.Application.Services.ResponseProcessing;
using MotorcycleRAG.Application.Services.Metrics;

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
    private readonly IAgentOrchestrator _orchestrator;
    private readonly ILogger<MotorcycleRagService> _logger;
    private readonly ITelemetryService _telemetryService;
    private readonly IQueryCacheService _cacheService;
    private readonly CacheConfiguration _cacheConfig;

    // Specialized services (injected)
    private readonly ClaimCitationService _citationService;
    private readonly QueryRefinementService _refinementService;
    private readonly ResponseLimitationAnalyzer _limitationAnalyzer;
    private readonly QueryCostCalculator _costCalculator;

    public MotorcycleRagService(
        IAgentOrchestrator orchestrator,
        ILogger<MotorcycleRagService> logger,
        ITelemetryService telemetryService,
        IQueryCacheService cacheService,
        IOptions<CacheConfiguration> cacheConfig,
        IAzureOpenAIClient openAIClient,
        ClaimCitationService citationService,
        QueryRefinementService refinementService,
        ResponseLimitationAnalyzer limitationAnalyzer,
        QueryCostCalculator costCalculator) {
        ArgumentNullException.ThrowIfNull(openAIClient);

        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _telemetryService = telemetryService ?? throw new ArgumentNullException(nameof(telemetryService));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        _cacheConfig = cacheConfig?.Value ?? throw new ArgumentNullException(nameof(cacheConfig));
        _citationService = citationService ?? throw new ArgumentNullException(nameof(citationService));
        _refinementService = refinementService ?? throw new ArgumentNullException(nameof(refinementService));
        _limitationAnalyzer = limitationAnalyzer ?? throw new ArgumentNullException(nameof(limitationAnalyzer));
        _costCalculator = costCalculator ?? throw new ArgumentNullException(nameof(costCalculator));
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

        // 4. Handle no-results (delegated)
        if (results.Length == 0 || string.IsNullOrWhiteSpace(answer)) {
            answer = _refinementService.GenerateNoResultsResponse(request.Query);
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
        // Calculate cost (delegated)
        var estimatedCost = _costCalculator.CalculateEstimatedCost(results, answer);

        var metrics = new QueryMetrics {
            ProcessingTimeMs = (int)duration.TotalMilliseconds,
            TotalDuration = duration,
            ResultsFound = results.Length,
            CacheHit = false,
            EstimatedCost = estimatedCost
        };

        // Extract claims and ensure citations (delegated)
        var citedResponse = await _citationService.GenerateCitedResponseAsync(answer, results, request.Query);

        // Convert to array for compatibility with MotorcycleQueryResponse and LimitationAnalyzer
        var citedResultsArray = citedResponse.Results.ToArray();

        // Analyze limitations and inject messages (delegated)
        var responseWithLimitations = _limitationAnalyzer.InjectLimitationMessages(
            citedResponse.Answer, citedResultsArray, metrics, queryId);

        var response = new MotorcycleQueryResponse {
            QueryId = queryId,
            Response = responseWithLimitations,
            Sources = citedResultsArray,
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

    // Helper methods removed - functionality moved to extracted services

    // Limitation analysis methods removed - functionality moved to ResponseLimitationAnalyzer

    // ClaimEvidence class removed - functionality moved to ClaimCitationService
}
