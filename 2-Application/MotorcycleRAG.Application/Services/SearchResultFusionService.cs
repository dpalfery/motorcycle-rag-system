using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.Application.Services;

/// <summary>
/// Service for fusing and ranking search results from multiple agents.
/// Implements partial-results aggregation for graceful degradation when sources become unavailable.
/// </summary>
public class SearchResultFusionService
{
    private readonly ILogger<SearchResultFusionService> _logger;

    public SearchResultFusionService(ILogger<SearchResultFusionService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Fuses and ranks search results from multiple agents.
    /// When degraded mode is active, adds metadata indicating which sources were unavailable.
    /// </summary>
    public async Task<SearchResult[]> FuseAndRankResultsAsync(
        List<SearchResult> results,
        string query,
        SearchParameters options,
        bool degradedMode)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(options);

        if (results.Count == 0)
        {
            _logger.LogDebug("No results to fuse for query: {Query}", query);
            return Array.Empty<SearchResult>();
        }

        _logger.LogDebug("Fusing {ResultCount} results for query: {Query}", results.Count, query);

        // Simple ranking: sort by relevance score descending
        var rankedResults = results
            .OrderByDescending(r => r.RelevanceScore)
            .Take(options.MaxResults)
            .ToArray();

        // Add degraded mode metadata if needed
        if (degradedMode)
        {
            foreach (var result in rankedResults)
            {
                result.Metadata["degradedMode"] = true;
            }
        }

        _logger.LogDebug("Fused and ranked {ResultCount} results for query: {Query}", rankedResults.Length, query);
        return rankedResults;
    }

    /// <summary>
    /// Fuses results from multiple agent execution attempts with source tracking
    /// </summary>
    public async Task<SearchResult[]> FuseResultsWithSourceTrackingAsync(
        List<SearchResult> aggregatedResults,
        string query,
        SearchParameters options,
        List<Application.Services.Telemetry.SourceExecutionStatus> sourceStatuses)
    {
        ArgumentNullException.ThrowIfNull(aggregatedResults);
        ArgumentNullException.ThrowIfNull(sourceStatuses);

        var degradedMode = sourceStatuses.Any(s => !s.Succeeded);
        var failedSources = sourceStatuses.Where(s => !s.Succeeded).ToList();
        var successfulSources = sourceStatuses.Where(s => s.Succeeded).ToList();

        // Add source tracking metadata
        foreach (var result in aggregatedResults)
        {
            result.Metadata["sourceExecutionStatus"] = new
            {
                TotalSources = sourceStatuses.Count,
                SuccessfulSources = successfulSources.Count,
                FailedSources = failedSources.Count,
                FailedSourceTypes = failedSources.Select(s => s.AgentType.ToString()).ToList(),
                SuccessfulSourceTypes = successfulSources.Select(s => s.AgentType.ToString()).ToList()
            };
        }

        return await FuseAndRankResultsAsync(aggregatedResults, query, options, degradedMode);
    }
}