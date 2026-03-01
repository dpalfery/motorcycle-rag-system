namespace MotorcycleRAG.Application.Pipeline;

/// <summary>
/// Application-layer contract for retrieving manual page assets.
/// </summary>
public interface IManualPageQueryService
{
    /// <summary>
    /// Retrieves a manual page image stream and its ETag for HTTP caching.
    /// </summary>
    /// <param name="manualId">The manual document identifier.</param>
    /// <param name="pageNumber">1-based page number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A result containing the page content stream and optional ETag.</returns>
    /// <exception cref="System.ArgumentOutOfRangeException">Thrown when <paramref name="pageNumber"/> is less than 1.</exception>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">Thrown when the requested page does not exist.</exception>
    Task<ManualPageResult> GetPageAsync(Guid manualId, int pageNumber, CancellationToken ct = default);
}