using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Models.DTOs.Web;

namespace MotorcycleRAG.Application.Services.Web;

/// <summary>
/// Extracts clean, structured content from HTML web pages
/// </summary>
public class WebContentExtractor
{
    private static readonly string[] DefaultSelectors = { "//p", "//article", "//div[@class*='content']", "//body" };
    private readonly ILogger<WebContentExtractor> _logger;

    public WebContentExtractor(ILogger<WebContentExtractor> logger)
    {
        _logger = logger;
    }

    public IReadOnlyCollection<ExtractedContent> ExtractFromHtml(
        string htmlContent,
        string customSelector)
        => ExtractFromHtml(htmlContent, customSelector, 5);

    public IReadOnlyCollection<ExtractedContent> ExtractFromHtml(
        string htmlContent,
        string customSelector,
        int maxResults)
    {
        var results = new List<ExtractedContent>();

        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(htmlContent);

            var contentNodes = GetContentNodes(doc, customSelector);

            if (contentNodes != null)
            {
                foreach (var node in contentNodes.Take(maxResults))
                {
                    var cleanText = ExtractCleanText(node);
                    if (!string.IsNullOrWhiteSpace(cleanText) && cleanText.Length >= 20)
                    {
                        results.Add(new ExtractedContent
                        {
                            Text = cleanText,
                            NodePath = node.XPath
                        });
                    }
                }
            }

            // Fallback to full document if no content found
            if (results.Count == 0)
            {
                var fullContent = ExtractCleanText(doc.DocumentNode);
                if (!string.IsNullOrWhiteSpace(fullContent) && fullContent.Length > 50)
                {
                    results.Add(new ExtractedContent
                    {
                        Text = fullContent.AsSpan(0, Math.Min(500, fullContent.Length)).ToString(),
                        NodePath = "//body",
                        IsFallback = true
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract content from HTML");
        }

        return results;
    }

    public async Task<ExtractedContent> ExtractContentAsync(string htmlContent)
    {
        var results = ExtractFromHtml(htmlContent, "//p", 1);
        return results.FirstOrDefault() ?? new ExtractedContent();
    }

    private HtmlNodeCollection? GetContentNodes(HtmlDocument doc, string customSelector)
    {
        return DefaultSelectors
            .Prepend(customSelector)
            .Where(s => !string.IsNullOrEmpty(s))
            .Select(selector => doc.DocumentNode.SelectNodes(selector))
            .FirstOrDefault(nodes => nodes != null && nodes.Count > 0);
    }

    private string ExtractCleanText(HtmlNode node)
    {
        var text = node.InnerText;
        text = HtmlEntity.DeEntitize(text);
        text = Regex.Replace(text, @"\s+", " ");
        text = text.Trim();

        if (text.Length > 500)
        {
            text = string.Concat(text.AsSpan(0, 500), "...");
        }

        return text;
    }
}
