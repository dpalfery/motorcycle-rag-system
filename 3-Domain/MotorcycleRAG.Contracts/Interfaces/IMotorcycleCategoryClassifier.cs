using MotorcycleRAG.Domain.ValueObjects;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Resolves a <see cref="MotorcycleCategory"/> for a (Make, Model) pair via the D7
/// cache-on-miss flow (authoritative SQL cache -&gt; local LLM -&gt; write-back).
/// </summary>
/// <remarks>
/// <para>
/// This interface lives in Contracts so that the Persistence-layer
/// <c>ChunkIndexingService</c> can consume category resolution WITHOUT depending on the
/// Application layer (where the <c>MotorcycleCategoryClassifier</c> implementation
/// lives). This respects the Dependency Rule: Persistence -&gt; Contracts (allowed);
/// Persistence -&gt; Application (forbidden).
/// </para>
/// <para>
/// Implementations MUST be non-throwing on LLM/cache failure and return the configured
/// fallback category (R4) so that a transient outage can never park an indexing job.
/// </para>
/// </remarks>
public interface IMotorcycleCategoryClassifier
{
    /// <summary>
    /// Resolves the <see cref="MotorcycleCategory"/> for the given (Make, Model).
    /// Never throws &mdash; LLM/cache failure yields the configured fallback category.
    /// </summary>
    /// <param name="make">Raw manufacturer name &mdash; will be normalized.</param>
    /// <param name="model">Raw model designation &mdash; will be normalized.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A defined <see cref="MotorcycleCategory"/>.</returns>
    Task<MotorcycleCategory> ResolveCategoryAsync(
        string make,
        string model,
        CancellationToken cancellationToken = default);
}
