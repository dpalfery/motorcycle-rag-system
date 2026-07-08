using Azure;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Core.Utilities;
using MotorcycleRAG.Domain.ValueObjects;

namespace MotorcycleRAG.Persistence.Azure.Search;

/// <summary>
/// Default <see cref="ISearchClientFactory"/>. Lazily creates one
/// <see cref="SearchClient"/> per category index and caches it for the lifetime of this
/// factory instance. A single <see cref="DefaultAzureCredential"/> is shared across all
/// clients (it is the documented cold-auth source and should be reused, not recreated
/// per request).
/// </summary>
public sealed class SearchClientFactory : ISearchClientFactory
{
    private readonly Uri _searchServiceEndpoint;
    private readonly DefaultAzureCredential _credential;
    private readonly SearchIndexClient _indexClient;
    private readonly Dictionary<MotorcycleCategory, SearchClient> _clientsByCategory = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchClientFactory"/>.
    /// </summary>
    /// <param name="azureOptions">Bound <see cref="AzureFoundryOptions"/> carrying the Search service endpoint.</param>
    /// <param name="indexClient">
    /// The service-level (not index-bound) <see cref="SearchIndexClient"/> used by
    /// <see cref="IndexExistsAsync"/> to verify index existence without creating indexes.
    /// </param>
    public SearchClientFactory(IOptions<AzureFoundryOptions> azureOptions, SearchIndexClient indexClient)
    {
        ArgumentNullException.ThrowIfNull(azureOptions);
        var azureConfig = azureOptions.Value ?? throw new ArgumentException("AzureFoundryOptions value is null.", nameof(azureOptions));

        if (!Uri.TryCreate(azureConfig.SearchServiceEndpoint, UriKind.Absolute, out var endpoint))
        {
            throw new ArgumentException(
                $"AzureAI:SearchServiceEndpoint '{azureConfig.SearchServiceEndpoint}' is not a valid absolute URI.",
                nameof(azureOptions));
        }

        _searchServiceEndpoint = endpoint;
        // DefaultAzureCredential is intentionally shared (created once) — recreating it per client
        // multiplies the cold-auth latency that is the known freeze source (plan §1 root-cause #7).
        _credential = new DefaultAzureCredential();
        _indexClient = indexClient ?? throw new ArgumentNullException(nameof(indexClient));
    }

    /// <inheritdoc />
    public MotorcycleCategory DefaultCategory => MotorcycleCategory.Sport;

    /// <inheritdoc />
    public IReadOnlyCollection<MotorcycleCategory> AllCategories => MotorcycleCategory.All;

    /// <inheritdoc />
    public SearchClient GetClient(MotorcycleCategory category)
    {
        // Normalize an undefined (default) category to the configured default so a caller passing
        // an uninitialized value object never targets a "motorcycle-" index.
        var resolved = category.IsDefined ? category : DefaultCategory;

        lock (_clientsByCategory)
        {
            if (!_clientsByCategory.TryGetValue(resolved, out var client))
            {
                client = new SearchClient(_searchServiceEndpoint, GetIndexName(resolved), _credential);
                _clientsByCategory[resolved] = client;
            }

            return client;
        }
    }

    /// <inheritdoc />
    public SearchClient GetDefaultClient() => GetClient(DefaultCategory);

    /// <inheritdoc />
    public string GetIndexName(MotorcycleCategory category)
    {
        var resolved = category.IsDefined ? category : DefaultCategory;
        return MotorcycleSearchIndexNaming.ForCategory(resolved.Value);
    }

    /// <inheritdoc />
    public async Task<bool> IndexExistsAsync(MotorcycleCategory category, CancellationToken ct = default)
    {
        var resolved = category.IsDefined ? category : DefaultCategory;
        var indexName = GetIndexName(resolved);

        try
        {
            // GetIndexAsync throws RequestFailedException(404) when the index is absent.
            // It never creates the index (D1/D4).
            await _indexClient.GetIndexAsync(indexName, ct).ConfigureAwait(false);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }
}
