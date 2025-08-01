using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Core.Interfaces;
using MotorcycleRAG.Core.Models;

namespace MotorcycleRAG.Core.Services;

/// <summary>
/// Main service coordinating the complete retrieval-augmented generation (RAG) pipeline for motorcycle queries.
/// </summary>
public sealed class MotorcycleRAGService : IMotorcycleRAGService
{
    private readonly IAgentOrchestrator _orchestrator;
    private readonly ILogger<MotorcycleRAGService> _logger;
    private readonly ITelemetryService _telemetryService;

    public MotorcycleRAGService(IAgentOrchestrator orchestrator, ILogger<MotorcycleRAGService> logger, ITelemetryService telemetryService)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _telemetryService = telemetryService ?? throw new ArgumentNullException(nameof(telemetryService));
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

        // Build a lightweight search context from the incoming request.
        var context = new SearchContext
        {
            SessionId = request.Context.SessionId,
            Preferences = request.Preferences,
            QueryContext = request.Context
        };

        // 1. Execute orchestrated search across all agents.
        var results = await _orchestrator.ExecuteSequentialSearchAsync(request.Query, context);

        // 2. Generate final natural-language response using large language model.
        var answer = await _orchestrator.GenerateResponseAsync(results, request.Query);

        stopwatch.Stop();

        // Populate basic metrics. In a full implementation we'd enrich these with detailed token/cost stats.
        var metrics = new QueryMetrics
        {
            TotalDuration = stopwatch.Elapsed,
            ResultsFound = results.Length
        };

        var response = new MotorcycleQueryResponse
        {
            QueryId = Guid.NewGuid().ToString("N"),
            Response = answer,
            Sources = results,
            Metrics = metrics,
            GeneratedAt = DateTime.UtcNow
        };

        _logger.LogInformation("Query processed. {Results} results, duration {Duration} ms", results.Length, stopwatch.ElapsedMilliseconds);
        
        // Track telemetry
        _telemetryService.TrackQuery(response.QueryId, request.Query, stopwatch.Elapsed, results.Length, response.Metrics.EstimatedCost);

        return response;
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> GetHealthAsync()
    {
        // For now we expose a very lightweight health indicator. Additional component checks can be added here later.
        var result = new HealthCheckResult
        {
            IsHealthy = true,
            Status = "OK",
            Details =
            {
                ["Timestamp"] = DateTime.UtcNow
            }
        };

        return Task.FromResult(result);
    }
}