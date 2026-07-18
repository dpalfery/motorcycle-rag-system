using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Application.Services.Citations;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.Application.Services;

/// <summary>
/// Main service coordinating the complete retrieval-augmented generation (RAG) pipeline for motorcycle queries.
/// Enhanced with caching and performance optimizations.
/// </summary>
public sealed class MotorcycleRagService : IMotorcycleRagService
{
    private readonly IAgentOrchestrator _orchestrator;
    private readonly ILogger<MotorcycleRagService> _logger;
    private readonly MotorcycleRagServiceDependencies _dependencies;

    public MotorcycleRagService(
        IAgentOrchestrator orchestrator,
        ILogger<MotorcycleRagService> logger,
        MotorcycleRagServiceDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(orchestrator);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(dependencies);

        _orchestrator = orchestrator;
        _logger = logger;
        _dependencies = dependencies;
    }

    /// <inheritdoc />
    public Task<MotorcycleQueryResponse> SearchAsync(MotorcycleQueryRequest request) => QueryAsync(request);

    /// <inheritdoc />
    public async Task<MotorcycleQueryResponse> QueryAsync(MotorcycleQueryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            throw new ArgumentException("Query cannot be null or empty", nameof(request));
        }

        var queryId = Guid.NewGuid().ToString("N");
        _logger.LogInformation("[{QueryId}] Processing motorcycle RAG query with length {QueryLength}",
            queryId, request.Query.Length);

        var stopwatch = Stopwatch.StartNew();

        // 1. Check cache first
        var cachedResponse = await TryGetCachedResponseAsync(request, queryId);
        if (cachedResponse != null)
        {
            stopwatch.Stop();
            _logger.LogInformation(
                "[{QueryId}] Cache hit for query. Duration: {Duration}ms",
                queryId,
                stopwatch.ElapsedMilliseconds);

            return cachedResponse;
        }

        // 2. Execute orchestrated search — answer is embedded in the returned results
        var (results, answer) = await ExecuteSearchWithAnswerAsync(request);

        // 3. Handle no-results (delegated) — skip if Foundry returned an answer
        if (string.IsNullOrWhiteSpace(answer))
        {
            answer = _dependencies.RefinementService.GenerateNoResultsResponse(request.Query);
        }

        stopwatch.Stop();
        return await FinalizeResponseAsync(request, queryId, results, answer ?? string.Empty, stopwatch.Elapsed);
    }

    private async Task<MotorcycleQueryResponse?> TryGetCachedResponseAsync(MotorcycleQueryRequest request, string queryId)
    {
        if (!_dependencies.CacheConfig.EnableCaching)
        {
            return null;
        }

        var cacheKey = _dependencies.CacheService.GenerateCacheKey(request);
        var cachedResponse = await _dependencies.CacheService.GetAsync(cacheKey);
        if (cachedResponse == null)
        {
            return null;
        }

        // Update cached response with new query ID and timestamp
        cachedResponse.QueryId = queryId;
        cachedResponse.GeneratedAt = DateTime.UtcNow;

        if (cachedResponse.Metrics != null)
        {
            cachedResponse.Metrics.CacheHit = true;
        }

        _dependencies.TelemetryService.TrackQuery(
            queryId,
            request.Query,
            TimeSpan.Zero,
            cachedResponse.Sources?.Length ?? 0,
            cachedResponse.Metrics?.EstimatedCost ?? 0);

        return cachedResponse;
    }

    private async Task<(SearchResult[] Results, string Answer)> ExecuteSearchWithAnswerAsync(MotorcycleQueryRequest request)
    {
        var context = new SearchContext
        {
            SessionId = request.Context?.SessionId ?? Guid.NewGuid().ToString(),
            Preferences = request.Preferences,
            QueryContext = request.Context ?? new QueryContext()
        };

        var results = await _orchestrator.ExecuteSequentialSearchAsync(request.Query, context);

        // Extract the Foundry synthesized answer from the result marked with FoundryAnswer
        var foundryResult = Array.Find(results,
            r => r.Metadata.TryGetValue("FoundryAnswer", out var v) && v is true);

        var answer = foundryResult?.Content ?? string.Empty;

        // Strip the synthetic FoundryAnswer result from the sources list if present
        var sources = foundryResult != null
            ? results.Where(r => r != foundryResult).ToArray()
            : results;

        return (sources, answer);
    }

    private async Task<MotorcycleQueryResponse> FinalizeResponseAsync(
        MotorcycleQueryRequest request,
        string queryId,
        SearchResult[] results,
        string answer,
        TimeSpan duration)
    {
        if (_dependencies.QuestionValidationState.Result?.MaySearch == false)
        {
            return FinalizeClarificationResponse(request, queryId, duration, answer);
        }

        // Calculate cost (delegated)
        var estimatedCost = _dependencies.CostCalculator.CalculateEstimatedCost(results, answer);

        var metrics = new QueryMetrics
        {
            ProcessingTimeMs = (int)duration.TotalMilliseconds,
            TotalDuration = duration,
            ResultsFound = results.Length,
            CacheHit = false,
            EstimatedCost = estimatedCost
        };

        // Extract claims and ensure citations (delegated)
        var citedResponse = await _dependencies.CitationService.GenerateCitedResponseAsync(answer, results, request.Query);

        // Convert to array for compatibility with MotorcycleQueryResponse and LimitationAnalyzer
        var citedResultsArray = citedResponse.Results.ToArray();

        // Analyze limitations and inject messages (delegated)
        var responseWithLimitations = _dependencies.LimitationAnalyzer.InjectLimitationMessages(
            citedResponse.Answer,
            citedResultsArray,
            metrics,
            queryId);

        var response = new MotorcycleQueryResponse
        {
            QueryId = queryId,
            Response = responseWithLimitations,
            Sources = citedResultsArray,
            Metrics = metrics,
            GeneratedAt = DateTime.UtcNow,
            ModelUsed = "DeepSeek-V4-Flash"
        };

        // Cache if appropriate
        if (_dependencies.CacheConfig.EnableCaching && ShouldCacheResponse(response))
        {
            var cacheKey = _dependencies.CacheService.GenerateCacheKey(request);
            var expiration = DetermineCacheExpiration(response);
            await _dependencies.CacheService.SetAsync(cacheKey, response, expiration);
        }

        _dependencies.TelemetryService.TrackQuery(queryId, request.Query, duration, results.Length, estimatedCost);
        return response;
    }

    private MotorcycleQueryResponse FinalizeClarificationResponse(
        MotorcycleQueryRequest request,
        string queryId,
        TimeSpan duration,
        string fallbackAnswer)
    {
        var validation = _dependencies.QuestionValidationState.Result;
        var responseText = validation?.ClarificationQuestion;
        if (string.IsNullOrWhiteSpace(responseText))
        {
            responseText = string.IsNullOrWhiteSpace(fallbackAnswer)
                ? "I need a little more detail before I can answer accurately."
                : fallbackAnswer;
        }

        var metrics = new QueryMetrics
        {
            ProcessingTimeMs = (int)duration.TotalMilliseconds,
            TotalDuration = duration,
            ResultsFound = 0,
            CacheHit = false,
            EstimatedCost = 0
        };

        _dependencies.TelemetryService.TrackQuery(queryId, request.Query, duration, 0, 0);

        return new MotorcycleQueryResponse
        {
            QueryId = queryId,
            ResponseType = "Clarification",
            Response = responseText,
            Suggestions = validation?.Suggestions ?? Array.Empty<QueryClarificationSuggestion>(),
            Sources = Array.Empty<SearchResult>(),
            Metrics = metrics,
            GeneratedAt = DateTime.UtcNow,
            ModelUsed = "DeepSeek-V4-Flash"
        };
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> GetHealthAsync()
    {
        var result = new HealthCheckResult
        {
            IsHealthy = true,
            Status = "OK",
            Details = { ["Timestamp"] = DateTime.UtcNow }
        };

        if (_dependencies.CacheConfig.EnableCaching)
        {
            try
            {
                var cacheStats = await _dependencies.CacheService.GetStatisticsAsync();
                result.Details["Cache.HitRatio"] = cacheStats.HitRatio.ToString("0.00%", CultureInfo.InvariantCulture);
                result.Details["Cache.TotalEntries"] = cacheStats.TotalEntries.ToString();
                result.Details["Cache.MemoryUsage"] =
                    (cacheStats.TotalMemoryUsage / 1024d / 1024d).ToString("F1", CultureInfo.InvariantCulture) + "MB";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get cache statistics for health check");
                result.Details["Cache.Status"] = "Error";
            }
        }

        return result;
    }

    private static bool ShouldCacheResponse(MotorcycleQueryResponse response)
    {
        return response.Sources?.Length > 0 &&
               response.Metrics?.ProcessingTimeMs < 30000 && // Less than 30 seconds
               !string.IsNullOrWhiteSpace(response.Response);
    }

    private TimeSpan DetermineCacheExpiration(MotorcycleQueryResponse response)
    {
        // Use longer expiration for high-quality responses
        if (response.Sources?.Length > 3 && response.Metrics?.ProcessingTimeMs < 5000)
        {
            return _dependencies.CacheConfig.LongTermExpiration;
        }

        return _dependencies.CacheConfig.DefaultExpiration;
    }

    // NOTE: Used by integration tests via reflection.
    internal ManualPdfCitationLocator? CreateLocatorForSource(SearchResult source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.Source.AgentType != SearchAgentType.PDFSearch)
        {
            return null;
        }

        var locator = new ManualPdfCitationLocator
        {
            DocumentId = source.Source.DocumentId,
            Title = source.Source.SourceName,
            PageNumber = 1,
            SourceUrl = source.Source.SourceUrl?.ToString() ?? string.Empty,
            PublicationDate = source.Source.LastUpdated
        };

        if (source.Metadata == null || source.Metadata.Count == 0)
        {
            return locator;
        }

        if (source.Metadata.TryGetValue("PageNumber", out var pn) && pn is int pageNum && pageNum > 0)
        {
            locator.PageNumber = pageNum;
        }

        if (source.Metadata.TryGetValue("PageRange", out var pr) && pr is string pageRange)
        {
            locator.PageRange = pageRange;
        }

        if (source.Metadata.TryGetValue("PrimarySection", out var ps) && ps is string primarySection)
        {
            locator.PrimarySection = primarySection;
        }

        if (source.Metadata.TryGetValue("SectionLevel", out var sl) && sl is int sectionLevel)
        {
            locator.SectionLevel = sectionLevel;
        }

        if (source.Metadata.TryGetValue("SectionHeadings", out var sh) && sh is string[] sectionHeadings && sectionHeadings.Length > 0)
        {
            locator.SectionHeadings = sectionHeadings;
            locator.PrimarySection = sectionHeadings[0];
        }

        if (source.Metadata.TryGetValue("TableCaption", out var tc) && tc is string tableCaption)
        {
            locator.TableCaption = tableCaption;
        }

        if (source.Metadata.TryGetValue("ChunkIndex", out var ci) && ci is int chunkIndex)
        {
            locator.ChunkIndex = chunkIndex;
        }

        return locator;
    }
}
