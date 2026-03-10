using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.Application.Services.Web;

/// <summary>
/// Enhances search queries using AI to generate motorcycle-specific search terms
/// </summary>
public class WebSearchTermEnhancer
{
    private readonly IAzureFoundryClient _openAIClient;
    private readonly string _modelName;
    private readonly ILogger<WebSearchTermEnhancer> _logger;

    public WebSearchTermEnhancer(
        IAzureFoundryClient openAIClient,
        IOptions<WebSearchOptions> options,
        ILogger<WebSearchTermEnhancer> logger)
        : this(
            openAIClient,
            (options ?? throw new ArgumentNullException(nameof(options))).Value.SearchTermModel,
            logger)
    {
    }

    public WebSearchTermEnhancer(
        IAzureFoundryClient openAIClient,
        string modelName,
        ILogger<WebSearchTermEnhancer> logger)
    {
        ArgumentNullException.ThrowIfNull(openAIClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        ArgumentNullException.ThrowIfNull(logger);

        _openAIClient = openAIClient;
        _modelName = modelName;
        _logger = logger;
    }

    public async Task<List<string>> GenerateSearchTermsAsync(
        string originalQuery,
        CancellationToken cancellationToken)
    {
        try
        {
            var prompt = $@"
Generate 3-5 specific search terms for finding authoritative motorcycle information about: '{originalQuery}'

Focus on:
- Official manufacturer websites
- Technical specifications
- Maintenance procedures
- Performance data
- Safety information

Return only the search terms, one per line, without explanations.
";

            var response = await _openAIClient.GetChatCompletionAsync(_modelName, prompt, cancellationToken);
            var searchTerms = response.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(term => term.Trim())
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .Take(5)
                .ToList();

            if (!searchTerms.Contains(originalQuery))
            {
                searchTerms.Insert(0, originalQuery);
            }

            _logger.LogDebug("Generated {Count} search terms", searchTerms.Count);
            return searchTerms;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enhance search terms, using original");
            return new List<string> { originalQuery };
        }
    }

    public async Task<string> EnhanceSearchTermsAsync(string query)
    {
        var terms = await GenerateSearchTermsAsync(query, CancellationToken.None);
        return terms.FirstOrDefault() ?? query;
    }
}