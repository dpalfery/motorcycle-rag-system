using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.Persistence.Web;

/// <summary>
/// Fetches trusted-source HTML through the container-managed web-search client.
/// </summary>
public sealed class TrustedWebContentFetcher : ITrustedWebContentFetcher
{
    /// <summary>
    /// The named client's registration name.
    /// </summary>
    public const string HttpClientName = nameof(TrustedWebContentFetcher);

    private readonly HttpClient _httpClient;
    private readonly IWebContentExtractor _extractor;

    public TrustedWebContentFetcher(HttpClient httpClient, IWebContentExtractor extractor)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(extractor);
        _httpClient = httpClient;
        _extractor = extractor;
    }

    /// <inheritdoc />
    public async Task<string> FetchAsync(Uri sourceUrl, string searchTerm, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceUrl);
        ArgumentNullException.ThrowIfNull(searchTerm);

        var html = await _httpClient.GetStringAsync(sourceUrl, cancellationToken);
        var source = new TrustedSourceOptions
        {
            Name = sourceUrl.ToString(),
            BaseUrl = sourceUrl,
            SearchUrlTemplate = sourceUrl,
            ContentSelector = "//p|//article|//div[@class='content']",
            CredibilityScore = 0.5f,
        };

        return _extractor.Extract(html, searchTerm, source);
    }
}
