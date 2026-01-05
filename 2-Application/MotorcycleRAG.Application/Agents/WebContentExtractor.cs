using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using HtmlAgilityPack;
using System.Text.RegularExpressions;

namespace MotorcycleRAG.Application.Agents;

public class WebContentExtractor {
    private readonly ILogger _logger;

    public WebContentExtractor(ILogger logger) {
        _logger = logger;
    }

    public List<SearchResult> ExtractSearchResults(string htmlContent, string searchTerm, TrustedSourceOptions source) {
        var results = new List<SearchResult>();

        try {
            var doc = new HtmlDocument();
            doc.LoadHtml(htmlContent);

            var selectors = new[] { source.ContentSelector, "//p", "//article", "//div[@class*='content']", "//body" };
            HtmlNodeCollection? contentNodes = null;

            foreach (var selector in selectors) {
                contentNodes = doc.DocumentNode.SelectNodes(selector);
                if (contentNodes != null && contentNodes.Count > 0)
                    break;
            }

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
                var fullContent = ExtractCleanText(doc.DocumentNode);
                if (!string.IsNullOrWhiteSpace(fullContent) && fullContent.Length > 50) {
                    var sr2 = new SearchResult {
                        Id = $"web_{Guid.NewGuid()}",
                        Content = fullContent.Substring(0, Math.Min(500, fullContent.Length)),
                        RelevanceScore = 0.6f,
                        Source = new SearchSource {
                            AgentType = SearchAgentType.WebSearch,
                            SourceName = source.Name,
                            SourceUrl = source.BaseUrl?.ToString(),
                            LastUpdated = DateTime.UtcNow
                        },
                        GeneratedAt = DateTime.UtcNow
                    };

                    sr2.Metadata["searchTerm"] = searchTerm;
                    sr2.Metadata["sourceType"] = "web";
                    sr2.Metadata["credibilityScore"] = source.CredibilityScore;
                    sr2.Metadata["extractedAt"] = DateTime.UtcNow;
                    sr2.Metadata["fallbackContent"] = true;

                    foreach (var h in ExtractHighlights(fullContent, searchTerm))
                        sr2.Highlights.Add(h);

                    results.Add(sr2);
                }
            }
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "Failed to extract results from {Source}", source.Name);
        }

        return results;
    }

    private string ExtractCleanText(HtmlNode node) {
        var text = node.InnerText;
        text = HtmlEntity.DeEntitize(text);
        text = Regex.Replace(text, @"\s+", " ");
        text = text.Trim();
        if (text.Length > 500) {
            text = text.Substring(0, 500) + "...";
        }
        return text;
    }

    private bool IsRelevantContent(string content, string searchTerm) {
        if (string.IsNullOrWhiteSpace(content) || content.Length < 20)
            return false;

        var motorcycleKeywords = new[] { "motorcycle", "bike", "engine", "horsepower", "cc", "specifications", "honda", "yamaha", "kawasaki", "ducati", "bmw", "suzuki" };
        var searchWords = searchTerm.ToLower().Split(' ');

        var contentLower = content.ToLower();

        var hasMotorcycleKeyword = motorcycleKeywords.Any(keyword => contentLower.Contains(keyword));
        var hasSearchTerm = searchWords.Any(word => word.Length > 2 && contentLower.Contains(word));

        return hasMotorcycleKeyword || hasSearchTerm;
    }

    private float CalculateRelevanceScore(string content, string searchTerm) {
        var contentLower = content.ToLower();
        var searchWords = searchTerm.ToLower().Split(' ');

        var score = 0.3f;

        if (contentLower.Contains(searchTerm.ToLower())) {
            score += 0.3f;
        }

        var wordMatches = searchWords.Count(word => contentLower.Contains(word));
        score += (wordMatches / (float)searchWords.Length) * 0.2f;

        var motorcycleTerms = new[] { "specifications", "performance", "engine", "horsepower", "torque" };
        var motorcycleMatches = motorcycleTerms.Count(term => contentLower.Contains(term));
        score += (motorcycleMatches / (float)motorcycleTerms.Length) * 0.2f;

        return Math.Min(1.0f, score);
    }

    private List<string> ExtractHighlights(string content, string query) {
        var highlights = new List<string>();
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var word in words.Take(3)) {
            var index = content.IndexOf(word, StringComparison.OrdinalIgnoreCase);
            if (index >= 0) {
                var start = Math.Max(0, index - 30);
                var length = Math.Min(80, content.Length - start);
                var highlight = content.Substring(start, length);

                highlights.Add($"...{highlight}...");
            }
        }

        return highlights.Take(2).ToList();
    }
}
