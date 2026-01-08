using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Application.Services.ResponseProcessing;

/// <summary>
/// Analyzes query results for limitations and generates user-friendly messages
/// </summary>
public class ResponseLimitationAnalyzer
{
    private readonly ILogger<ResponseLimitationAnalyzer> _logger;

    public ResponseLimitationAnalyzer(ILogger<ResponseLimitationAnalyzer> logger)
    {
        _logger = logger;
    }

    public string InjectLimitationMessages(string response, SearchResult[] results, QueryMetrics metrics, string queryId)
    {
        var messages = AnalyzeLimitations(results, metrics, queryId);

        if (messages.Count == 0)
        {
            return response;
        }

        var messageText = string.Join("\n\n", messages);
        return $"""
{messageText}

---

{response}
""";
    }

    private List<string> AnalyzeLimitations(SearchResult[] results, QueryMetrics metrics, string queryId)
    {
        var messages = new List<string>();

        if (results == null || results.Length == 0)
        {
            return messages; // Handled elsewhere
        }

        // Analyze search pattern for source failures
        if (metrics?.SearchPattern != null)
        {
            var (executed, withResults, failed) = AnalyzePattern(metrics.SearchPattern);

            if (executed > 0 && withResults < executed && withResults > 0)
            {
                messages.Add($"⚠️ **Partial Results**: Some sources ({string.Join(", ", failed)}) unavailable.");
                _logger.LogInformation("[{QueryId}] Partial availability: {Available}/{Total}", queryId, withResults, executed);
            }
            else if (withResults == 0 && executed > 0)
            {
                messages.Add("❌ **Service Degradation**: Unable to retrieve results. Try rephrasing.");
                _logger.LogWarning("[{QueryId}] All sources failed", queryId);
            }
        }

        if (results.Length == 1)
        {
            messages.Add("ℹ️ **Limited Results**: Only one result found.");
        }

        if (results.All(r => r.RelevanceScore < 0.5f))
        {
            messages.Add("⚠️ **Low Confidence**: Low relevance scores. Consider rephrasing.");
        }

        return messages;
    }

    private (int Executed, int WithResults, List<string> Failed) AnalyzePattern(SearchPatternMetrics pattern)
    {
        var executed = 0;
        var withResults = 0;
        var failed = new List<string>();

        if (pattern.VectorSearchExecuted)
        {
            executed++;
            if (pattern.VectorResultsFound > 0) withResults++;
            else failed.Add("vector search (indexed specifications)");
        }
        if (pattern.WebSearchExecuted)
        {
            executed++;
            if (pattern.WebResultsFound > 0) withResults++;
            else failed.Add("web search (trusted sources)");
        }
        if (pattern.PDFSearchExecuted)
        {
            executed++;
            if (pattern.PDFResultsFound > 0) withResults++;
            else failed.Add("PDF manual search");
        }

        return (executed, withResults, failed);
    }
}