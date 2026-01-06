using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using HtmlAgilityPack;
using System.Text.RegularExpressions;

namespace MotorcycleRAG.Application.Agents;

public class WebContentExtractor {
    private readonly ILogger _logger;

    private static readonly string[] DefaultSelectors = { "//p", "//article", "//div[@class*='content']", "//body" };
    private static readonly string[] MotorcycleKeywords = { "motorcycle", "bike", "engine", "horsepower", "cc", "specifications", "honda", "yamaha", "kawasaki", "ducati", "bmw", "suzuki" };
    private static readonly string[] DetailKeywords = { "specifications", "performance", "engine", "horsepower", "torque" };
    private static readonly char[] SpaceSeparator = { ' ' };

    public WebContentExtractor(ILogger logger) {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public SearchResult[] ExtractSearchResults(string htmlContent, string searchTerm, TrustedSourceOptions source) {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(searchTerm);
        if (string.IsNullOrEmpty(htmlContent)) return Array.Empty<SearchResult>();

        var results = new List<SearchResult>();

        try {
            var doc = new HtmlDocument();
            doc.LoadHtml(htmlContent);

            var contentNodes = DefaultSelectors
                .Prepend(source.ContentSelector)
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(selector => doc.DocumentNode.SelectNodes(selector))
                .FirstOrDefault(nodes => nodes != null && nodes.Count > 0);

            if (contentNodes != null) {
                foreach (var node in contentNodes.Take(5)) {
                    var content = ExtractCleanText(node);
                    if (IsRelevantContent(content, searchTerm)) {
                        var sr = new SearchResult {
                            Id = $"web_{Guid.NewGuid()}",
                            Content = content,
                            RelevanceScore = CalculateRelevanceScore(content, searchTerm),
                            Source = new SearchSource {
                                AgentType = SearchAgentType.WebSearch,
                                SourceName = source.Name,
                                SourceUrl = source.BaseUrl?.ToString(),
                                LastUpdated = DateTime.UtcNow
                            },
                            GeneratedAt = DateTime.UtcNow
                        };

                        sr.Metadata["searchTerm"] = searchTerm;
                        sr.Metadata["sourceType"] = "web";
                        sr.Metadata["credibilityScore"] = source.CredibilityScore;
                        sr.Metadata["extractedAt"] = DateTime.UtcNow;

                        foreach (var h in ExtractHighlights(content, searchTerm))
                            sr.Highlights.Add(h);

                        results.Add(sr);
                    }
                }
            }

            if (results.Count == 0) {
                var fallbackResult = ExtractFallbackResult(doc, searchTerm, source);
                if (fallbackResult != null) {
                    results.Add(fallbackResult);
                }
            }
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "Failed to extract results from {Source}", source.Name);
        }

        return results.ToArray();
    }

    private string ExtractCleanText(HtmlNode node) {
        var text = node.InnerText;
        text = HtmlEntity.DeEntitize(text);
        text = Regex.Replace(text, @"\s+", " ");
        text = text.Trim();
        if (text.Length > 500) {
            text = string.Concat(text.AsSpan(0, 500), "...");
        }
        return text;
    }

    private bool IsRelevantContent(string content, string searchTerm) {
        if (string.IsNullOrWhiteSpace(content) || content.Length < 20)
            return false;

        var searchWords = searchTerm.ToUpperInvariant().Split(SpaceSeparator, StringSplitOptions.RemoveEmptyEntries);
        var contentUpper = content.ToUpperInvariant();

        var hasMotorcycleKeyword = MotorcycleKeywords.Any(keyword => contentUpper.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        var hasSearchTerm = searchWords.Any(word => word.Length > 2 && contentUpper.Contains(word));

        return hasMotorcycleKeyword || hasSearchTerm;
    }

    private float CalculateRelevanceScore(string content, string searchTerm) {
        var contentUpper = content.ToUpperInvariant();
        var searchWords = searchTerm.ToUpperInvariant().Split(SpaceSeparator, StringSplitOptions.RemoveEmptyEntries);

        var score = 0.3f;

        if (contentUpper.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)) {
            score += 0.3f;
        }

        var wordMatches = searchWords.Count(word => contentUpper.Contains(word));
        score += (wordMatches / (float)searchWords.Length) * 0.2f;

        var motorcycleMatches = DetailKeywords.Select(k => k.ToUpperInvariant()).Count(term => contentUpper.Contains(term));
        score += (motorcycleMatches / (float)DetailKeywords.Length) * 0.2f;

        return Math.Min(1.0f, score);
    }

    private IEnumerable<string> ExtractHighlights(string content, string query) {
        var highlights = new List<string>();
        var words = query.Split(SpaceSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var word in words.Take(3)) {
            var index = content.IndexOf(word, StringComparison.OrdinalIgnoreCase);
            if (index >= 0) {
                var start = Math.Max(0, index - 30);
                var length = Math.Min(80, content.Length - start);
                var highlight = string.Concat("...", content.AsSpan(start, length), "...");

                highlights.Add(highlight);
            }
        }

        return highlights.Take(2);
    }

    private SearchResult? ExtractFallbackResult(HtmlDocument doc, string searchTerm, TrustedSourceOptions source) {
        var fullContent = ExtractCleanText(doc.DocumentNode);
        if (string.IsNullOrWhiteSpace(fullContent) || fullContent.Length <= 50)
            return null;

        var sr = new SearchResult {
            Id = string.Concat("web_", Guid.NewGuid().ToString()),
            Content = fullContent.AsSpan(0, Math.Min(500, fullContent.Length)).ToString(),
            RelevanceScore = 0.6f,
            Source = new SearchSource {
                AgentType = SearchAgentType.WebSearch,
                SourceName = source.Name,
                SourceUrl = source.BaseUrl?.ToString(),
                LastUpdated = DateTime.UtcNow
            },
            GeneratedAt = DateTime.UtcNow
        };

        sr.Metadata["searchTerm"] = searchTerm;
        sr.Metadata["sourceType"] = "web";
        sr.Metadata["credibilityScore"] = source.CredibilityScore;
        sr.Metadata["extractedAt"] = DateTime.UtcNow;
        sr.Metadata["fallbackContent"] = true;

        foreach (var h in ExtractHighlights(fullContent, searchTerm))
            sr.Highlights.Add(h);

        return sr;
    }
}
