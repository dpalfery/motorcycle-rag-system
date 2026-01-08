using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Application.Services.Metrics;

/// <summary>
/// Calculates estimated costs for RAG query processing
/// </summary>
public class QueryCostCalculator
{
    private const decimal InputCostPer1K = 0.0015m;
    private const decimal OutputCostPer1K = 0.002m;

    public decimal CalculateEstimatedCost(SearchResult[] results, string response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var inputTokens = results.Sum(r => r.Content?.Length ?? 0) / 4;
        var outputTokens = response.Length / 4;

        var inputCost = (inputTokens / 1000m) * InputCostPer1K;
        var outputCost = (outputTokens / 1000m) * OutputCostPer1K;

        return inputCost + outputCost;
    }
}