using Microsoft.Extensions.Options;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.API.Configuration;

/// <summary>
/// Validator for Search configuration
/// </summary>
internal class SearchConfigurationValidator : IValidateOptions<SearchOptions>
{
    private const int MaxBatchIndexTimeoutSeconds = 600;

    ValidateOptionsResult IValidateOptions<SearchOptions>.Validate(string? name, SearchOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.IndexName))
            failures.Add("Search:IndexName is required");

        if (options.BatchSize <= 0 || options.BatchSize > 1000)
            failures.Add("Search:BatchSize must be between 1 and 1000");

        if (options.BatchIndexTimeoutSeconds <= 0 || options.BatchIndexTimeoutSeconds > MaxBatchIndexTimeoutSeconds)
            failures.Add($"Search:BatchIndexTimeoutSeconds must be between 1 and {MaxBatchIndexTimeoutSeconds}");

        if (options.MaxSearchResults <= 0 || options.MaxSearchResults > 100)
            failures.Add("Search:MaxSearchResults must be between 1 and 100");

        if (string.Equals(options.ChunkIndexingProvider, "InMemoryShim", StringComparison.OrdinalIgnoreCase)) {
            if (!Uri.TryCreate(options.InMemoryShimEndpoint, UriKind.Absolute, out var shimEndpoint)) {
                failures.Add("Search:InMemoryShimEndpoint must be a valid absolute URL when Search:ChunkIndexingProvider is InMemoryShim");
            }
            else if (!IsLoopbackHttpEndpoint(shimEndpoint)) {
                failures.Add("Search:InMemoryShimEndpoint must be an http or https loopback URL when Search:ChunkIndexingProvider is InMemoryShim");
            }
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    private static bool IsLoopbackHttpEndpoint(Uri endpoint)
    {
        if (endpoint.Scheme is not ("http" or "https"))
            return false;

        if (string.Equals(endpoint.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        return System.Net.IPAddress.TryParse(endpoint.Host, out var address)
            && System.Net.IPAddress.IsLoopback(address);
    }
}
