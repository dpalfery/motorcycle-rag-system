using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Application.Services.QueryProcessing;

/// <summary>
/// Analyzes failed queries and generates refinement suggestions
/// </summary>
public class QueryRefinementService
{
    public QueryRefinementService(ILogger<QueryRefinementService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
    }

    public string GenerateNoResultsResponse(string originalQuery)
    {
        ArgumentNullException.ThrowIfNull(originalQuery);

        var analysis = AnalyzeQuery(originalQuery);
        return FormatNoResultsResponse(originalQuery, analysis);
    }

    private QueryRefinementAnalysis AnalyzeQuery(string query)
    {
        var suggestions = new List<string>();
        var exampleQueries = new List<string>();

        // Analyze query characteristics
        if (query.Length < 10)
        {
            suggestions.Add("✅ **Be more specific**: Add more details.");
        }

        if (!ContainsMotorcycleTerms(query))
        {
            suggestions.Add("✅ **Add context**: Include 'motorcycle', 'specs', etc.");
        }

        if (!ContainsBrandOrModel(query))
        {
            suggestions.Add("✅ **Specify brands/models**: Include Honda, Yamaha, CBR1000RR, etc.");
            exampleQueries.Add("Honda CBR1000RR 2023 specifications");
            exampleQueries.Add("Yamaha YZF-R1 vs Kawasaki Ninja ZX-10R");
        }
        else
        {
            var subject = ExtractMainSubject(query);
            exampleQueries.Add($"{subject} specifications and performance");
            exampleQueries.Add($"{subject} maintenance schedule");
        }

        if (!ContainsYear(query))
        {
            suggestions.Add("✅ **Include year**: Add model year for accuracy.");
        }

        return new QueryRefinementAnalysis
        {
            OriginalQuery = query,
            Suggestions = suggestions,
            ExampleQueries = exampleQueries
        };
    }

    private string FormatNoResultsResponse(string query, QueryRefinementAnalysis analysis)
    {
        return $"""
# No Results Found

I couldn't find information for: "{query}"

## Suggestions to Improve Your Search
{string.Join("\n", analysis.Suggestions)}

## Example Queries:
{string.Join("\n", analysis.ExampleQueries.Select(q => $"- {q}"))}
""";
    }

    private bool ContainsMotorcycleTerms(string query)
    {
        var motorcycleTerms = new[] { "motorcycle", "bike", "specs", "specifications", "manual", "guide", "review", "comparison" };
        return motorcycleTerms.Any(term => query.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private bool ContainsBrandOrModel(string query)
    {
        var commonBrands = new[] { "Honda", "Yamaha", "Kawasaki", "Suzuki", "Ducati", "BMW", "Harley", "Triumph" };
        var modelIndicators = new[] { "CBR", "R1", "ZX", "GSX", "Panigale", "S1000", "Street", "Ninja" };

        return commonBrands.Any(brand => query.Contains(brand, StringComparison.OrdinalIgnoreCase)) ||
               modelIndicators.Any(model => query.Contains(model, StringComparison.OrdinalIgnoreCase));
    }

    private bool ContainsYear(string query)
    {
        return System.Text.RegularExpressions.Regex.IsMatch(query, @"\d{4}");
    }

    private string ExtractMainSubject(string query)
    {
        var commonBrands = new[] { "Honda", "Yamaha", "Kawasaki", "Suzuki", "Ducati", "BMW", "Harley", "Triumph" };
        var model = commonBrands.FirstOrDefault(b => query.Contains(b, StringComparison.OrdinalIgnoreCase));
        if (model != null) return model;

        var modelIndicators = new[] { "CBR", "R1", "ZX", "GSX", "Panigale", "S1000", "Street", "Ninja" };
        var indicator = modelIndicators.FirstOrDefault(m => query.Contains(m, StringComparison.OrdinalIgnoreCase));
        if (indicator != null) return indicator;

        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length > 0 ? words[0] : "motorcycle";
    }
}
