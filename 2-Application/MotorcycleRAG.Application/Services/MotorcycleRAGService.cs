using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Application.Caching;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Models;

namespace MotorcycleRAG.Application.Services;

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

    public MotorcycleRAGService(
        IAgentOrchestrator orchestrator, 
        ILogger<MotorcycleRAGService> logger, 
        ITelemetryService telemetryService,
        IQueryCacheService cacheService,
        IOptions<CacheConfiguration> cacheConfig)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _telemetryService = telemetryService ?? throw new ArgumentNullException(nameof(telemetryService));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        _cacheConfig = cacheConfig?.Value ?? throw new ArgumentNullException(nameof(cacheConfig));
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
                cachedResponse.Metrics!.ProcessingTimeMs = (int)stopwatch.ElapsedMilliseconds;
                cachedResponse.Metrics.CacheHit = true;

                _logger.LogInformation("Cache hit for query. Duration: {Duration}ms", stopwatch.ElapsedMilliseconds);
                
                // Track telemetry for cached response
                _telemetryService.TrackQuery(queryId, request.Query, stopwatch.Elapsed, 
                    cachedResponse.Sources?.Length ?? 0, cachedResponse.Metrics.EstimatedCost);

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

        stopwatch.Stop();

        // 5. Populate metrics with performance data
        var metrics = new QueryMetrics
        {
            ProcessingTimeMs = (int)stopwatch.ElapsedMilliseconds,
            TotalDuration = stopwatch.Elapsed,
            ResultsFound = results.Length,
            CacheHit = false,
            EstimatedCost = CalculateEstimatedCost(results, answer)
        };

        var response = new MotorcycleQueryResponse
        {
            QueryId = queryId,
            Response = answer,
            Sources = results,
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
            results.Length, stopwatch.ElapsedMilliseconds, metrics.EstimatedCost);
        
        // Track telemetry
        _telemetryService.TrackQuery(queryId, request.Query, stopwatch.Elapsed, results.Length, metrics.EstimatedCost);

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
