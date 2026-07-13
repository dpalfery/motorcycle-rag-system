using System.Text.RegularExpressions;
using HtmlAgilityPack;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.Persistence.Web;

/// <summary>
/// Extracts relevant motorcycle content from trusted-source HTML.
/// </summary>
public sealed class HtmlWebContentExtractor : IWebContentExtractor
{
    private static readonly string[] DefaultSelectors = ["//p", "//article", "//div[@class*='content']", "//body"];
    private static readonly string[] MotorcycleKeywords = ["motorcycle", "bike", "engine", "horsepower", "cc", "specifications", "honda", "yamaha", "kawasaki", "ducati", "bmw", "suzuki"];
    private static readonly char[] SpaceSeparator = [' '];

    /// <inheritdoc />
    public string Extract(string htmlContent, string searchTerm, TrustedSourceOptions source)
    {
        ArgumentNullException.ThrowIfNull(htmlContent);
        ArgumentNullException.ThrowIfNull(searchTerm);
        ArgumentNullException.ThrowIfNull(source);

        if (htmlContent.Length == 0)
        {
            return string.Empty;
        }

        var document = new HtmlDocument();
        document.LoadHtml(htmlContent);

        var results = GetContentNodes(document, source.ContentSelector)
            ?.Take(5)
            .Select(node => ExtractCleanText(node))
            .Where(content => IsRelevantContent(content, searchTerm))
            .ToArray()
            ?? [];

        return results.Length > 0
            ? string.Join("\n\n", results)
            : ExtractFallbackContent(document);
    }

    private static HtmlNodeCollection? GetContentNodes(HtmlDocument document, string? contentSelector) =>
        (string.IsNullOrEmpty(contentSelector) ? DefaultSelectors : DefaultSelectors.Prepend(contentSelector))
            .Where(selector => !string.IsNullOrEmpty(selector))
            .Select(selector => document.DocumentNode.SelectNodes(selector))
            .FirstOrDefault(nodes => nodes is { Count: > 0 });

    private static string ExtractCleanText(HtmlNode node)
    {
        var text = HtmlEntity.DeEntitize(node.InnerText);
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text.Length > 500
            ? string.Concat(text.AsSpan(0, 500), "...")
            : text;
    }

    private static bool IsRelevantContent(string content, string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(content) || content.Length < 20)
        {
            return false;
        }

        var searchWords = searchTerm.Split(SpaceSeparator, StringSplitOptions.RemoveEmptyEntries);
        return MotorcycleKeywords.Any(keyword => content.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            || searchWords.Any(word => word.Length > 2 && content.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    private static string ExtractFallbackContent(HtmlDocument document)
    {
        var fullContent = ExtractCleanText(document.DocumentNode);
        return fullContent.Length > 50
            ? fullContent.AsSpan(0, Math.Min(500, fullContent.Length)).ToString()
            : string.Empty;
    }
}
