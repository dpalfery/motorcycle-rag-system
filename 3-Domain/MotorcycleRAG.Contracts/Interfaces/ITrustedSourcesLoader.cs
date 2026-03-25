using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Loads trusted web sources that the WebSearchAgent should search at query time.
/// </summary>
public interface ITrustedSourcesLoader
{
    /// <summary>Returns the set of enabled trusted sources from the backing store.</summary>
    Task<TrustedSourceOptions[]> LoadAsync(CancellationToken ct = default);
}
