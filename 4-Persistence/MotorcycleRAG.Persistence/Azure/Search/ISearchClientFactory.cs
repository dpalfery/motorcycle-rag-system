using Azure.Search.Documents;
using MotorcycleRAG.Domain.ValueObjects;

namespace MotorcycleRAG.Persistence.Azure.Search;

/// <summary>
/// Builds (and caches) an Azure AI Search <see cref="SearchClient"/> bound to the
/// category-partitioned index for a given <see cref="MotorcycleCategory"/> (D4).
/// </summary>
/// <remarks>
/// <para>
/// This interface lives in <b>Persistence</b> (not Contracts) because it returns the
/// concrete <see cref="SearchClient"/> Azure SDK type. Contracts is forbidden from
/// referencing any infrastructure SDK (EF Core, Azure, SQL, HTTP), and adding the Azure
/// Search package to Contracts would violate the Dependency Rule. Every concrete-SearchClient
/// consumer lives in Persistence, so the abstraction is consumed entirely within this layer.
/// </para>
/// <para>
/// Index naming follows <see cref="MotorcycleRAG.Core.Utilities.MotorcycleSearchIndexNaming"/>
/// (<c>motorcycle-{category}</c>), matching the T1 Pulumi provisioning
/// (<c>motorcycle-dirt</c>, <c>motorcycle-touring</c>, <c>motorcycle-sport</c>,
/// <c>motorcycle-cruiser</c>).
/// </para>
/// </remarks>
public interface ISearchClientFactory
{
    /// <summary>
    /// The category used when a caller has no category context (matches the
    /// <c>MotorcycleCategoryClassifier</c> fallback so category-unknown writes land in the
    /// same partition the classifier would have chosen).
    /// </summary>
    MotorcycleCategory DefaultCategory { get; }

    /// <summary>
    /// All four category partitions, in canonical order.
    /// </summary>
    IReadOnlyCollection<MotorcycleCategory> AllCategories { get; }

    /// <summary>
    /// Returns the (cached) <see cref="SearchClient"/> for the given category's index.
    /// </summary>
    SearchClient GetClient(MotorcycleCategory category);

    /// <summary>
    /// Returns the <see cref="SearchClient"/> for <see cref="DefaultCategory"/>.
    /// </summary>
    SearchClient GetDefaultClient();

    /// <summary>
    /// Returns the Azure AI Search index name for the given category
    /// (e.g. <c>motorcycle-dirt</c>).
    /// </summary>
    string GetIndexName(MotorcycleCategory category);

    /// <summary>
    /// Asynchronously determines whether the category index exists on the Search service.
    /// Used by the T6 existence precheck before the first batch upload. This method
    /// <b>never</b> creates the index (per D1/D4); it only reads.
    /// </summary>
    /// <param name="category">The category whose index existence is being verified.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <c>true</c> if the index exists; <c>false</c> if the Search service returns 404.
    /// Other failures propagate as exceptions.
    /// </returns>
    Task<bool> IndexExistsAsync(MotorcycleCategory category, CancellationToken ct = default);
}
