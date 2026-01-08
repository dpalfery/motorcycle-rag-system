using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Application.Services.Telemetry;

/// <summary>
/// Tracks and reports degraded search mode (partial source failures)
/// </summary>
public class DegradedModeTracker
{
    private readonly ILogger<DegradedModeTracker> _logger;

    public DegradedModeTracker(
        ILogger<DegradedModeTracker> logger)
    {
        _logger = logger;
    }

    public void TrackSearchExecution(
        IReadOnlyCollection<SourceExecutionStatus> allStatuses,
        TimeSpan totalDuration,
        int totalResults)
    {
        ArgumentNullException.ThrowIfNull(allStatuses);

        var failedSources = allStatuses.Where(s => !s.Succeeded).ToList();
        var successfulSources = allStatuses.Where(s => s.Succeeded).ToList();
        var degradedMode = failedSources.Any();

        if (degradedMode)
        {
            _logger.LogWarning("Degraded mode: {FailedCount} sources failed, {SuccessCount} succeeded",
                failedSources.Count, successfulSources.Count);
        }
    }

    public void UpdateQueryContextMetrics(
        SearchContext context,
        Dictionary<SearchAgentType, (TimeSpan Duration, int ResultsFound)> metrics,
        bool degradedMode,
        IReadOnlyCollection<SourceExecutionStatus> failedSources,
        IReadOnlyCollection<SourceExecutionStatus> successfulSources)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(failedSources);
        ArgumentNullException.ThrowIfNull(successfulSources);

        if (context.QueryContext == null) return;

        context.QueryContext.AdditionalProperties["SearchPatternMetrics"] = CreateMetrics(metrics);
        context.QueryContext.AdditionalProperties["DegradedMode"] = degradedMode;

        if (degradedMode)
        {
            context.QueryContext.AdditionalProperties["FailedSources"] =
                failedSources.Select(s => new { s.AgentType, s.ErrorMessage }).ToList();
            context.QueryContext.AdditionalProperties["AvailableSources"] =
                successfulSources.Select(s => new { s.AgentType, s.ResultsCount }).ToList();
        }
    }

    private SearchPatternMetrics CreateMetrics(Dictionary<SearchAgentType, (TimeSpan Duration, int ResultsFound)> metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        return new SearchPatternMetrics
        {
            VectorSearchExecuted = metrics.ContainsKey(SearchAgentType.VectorSearch),
            WebSearchExecuted = metrics.ContainsKey(SearchAgentType.WebSearch),
            PDFSearchExecuted = metrics.ContainsKey(SearchAgentType.PDFSearch),
            VectorSearchTime = metrics.TryGetValue(SearchAgentType.VectorSearch, out var v) ? v.Duration : TimeSpan.Zero,
            WebSearchTime = metrics.TryGetValue(SearchAgentType.WebSearch, out var w) ? w.Duration : TimeSpan.Zero,
            PDFSearchTime = metrics.TryGetValue(SearchAgentType.PDFSearch, out var p) ? p.Duration : TimeSpan.Zero,
            VectorResultsFound = metrics.TryGetValue(SearchAgentType.VectorSearch, out var vr) ? vr.ResultsFound : 0,
            WebResultsFound = metrics.TryGetValue(SearchAgentType.WebSearch, out var wr) ? wr.ResultsFound : 0,
            PDFResultsFound = metrics.TryGetValue(SearchAgentType.PDFSearch, out var pr) ? pr.ResultsFound : 0
        };
    }
}