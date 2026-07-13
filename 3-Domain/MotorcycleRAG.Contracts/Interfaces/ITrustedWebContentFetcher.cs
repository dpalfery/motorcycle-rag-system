namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Retrieves and extracts content from a trusted web source.
/// </summary>
public interface ITrustedWebContentFetcher
{
    /// <summary>
    /// Fetches the source page and returns content relevant to the supplied search term.
    /// </summary>
    Task<string> FetchAsync(Uri sourceUrl, string searchTerm, CancellationToken cancellationToken);
}
