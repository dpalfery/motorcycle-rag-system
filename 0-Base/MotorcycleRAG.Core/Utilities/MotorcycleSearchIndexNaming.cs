namespace MotorcycleRAG.Core.Utilities;

/// <summary>
/// Canonical mapping between a motorcycle category wire-value (the lowercase
/// <c>"dirt" | "touring" | "sport" | "cruiser"</c> token) and its Azure AI Search
/// index name (<c>motorcycle-{category}</c>).
/// </summary>
/// <remarks>
/// <para>
/// This is the single source of truth for the index-naming convention introduced by
/// the category-partitioning decision (D4): one physical index per category. It lives
/// in the 0-Base shared kernel so that BOTH the Application layer (e.g. result labels)
/// and the Persistence layer (the <c>SearchClientFactory</c>) resolve identical names
/// without either layer reaching across a boundary. The helper takes plain strings so
/// the 0-Base project never needs to reference the Domain <c>MotorcycleCategory</c>
/// value object (the Dependency Rule forbids Base → Domain).
/// </para>
/// <para>
/// The factory (<c>ISearchClientFactory</c>) and any caller that already holds a typed
/// <c>MotorcycleCategory</c> should pass <c>category.Value</c> to
/// <see cref="ForCategory"/>. Callers without a category should use
/// <see cref="DefaultIndexName"/>.
/// </para>
/// </remarks>
public static class MotorcycleSearchIndexNaming
{
    /// <summary>
    /// Index-name prefix shared by every category-partitioned index.
    /// Final names are <c>motorcycle-dirt</c>, <c>motorcycle-touring</c>,
    /// <c>motorcycle-sport</c>, <c>motorcycle-cruiser</c> (matches the T1 Pulumi
    /// provisioning).
    /// </summary>
    public const string IndexNamePrefix = "motorcycle-";

    /// <summary>
    /// The canonical wire value used when no category is known. Matches the
    /// <c>MotorcycleCategoryClassifier</c> fallback (R4) so category-unknown routing
    /// lands in the same partition the classifier would have chosen.
    /// </summary>
    public const string DefaultCategoryWireValue = "sport";

    /// <summary>
    /// The fully-resolved index name for <see cref="DefaultCategoryWireValue"/>.
    /// </summary>
    public static string DefaultIndexName => ForCategory(DefaultCategoryWireValue);

    /// <summary>
    /// The four canonical category wire-values, in the same order as
    /// <c>MotorcycleCategory.All</c>.
    /// </summary>
    public static IReadOnlyCollection<string> AllCategoryWireValues { get; } =
    [
        "dirt",
        "touring",
        "sport",
        "cruiser"
    ];

    /// <summary>
    /// Resolves the Azure AI Search index name for a category wire-value.
    /// The value is trimmed and lower-cased; an empty/whitespace value yields the
    /// <see cref="DefaultIndexName"/> so callers never produce a bare
    /// <c>"motorcycle-"</c> index name.
    /// </summary>
    /// <param name="categoryWireValue">
    /// The category wire-value (e.g. <c>"dirt"</c>, <c>"Dirt"</c>). Pass
    /// <c>MotorcycleCategory.Value</c> when a typed value object is available.
    /// </param>
    /// <returns>The index name, e.g. <c>motorcycle-dirt</c>.</returns>
    public static string ForCategory(string? categoryWireValue)
    {
        var normalized = (categoryWireValue ?? string.Empty).Trim().ToLowerInvariant();
        return normalized.Length == 0
            ? DefaultIndexName
            : IndexNamePrefix + normalized;
    }

    /// <summary>
    /// Determines whether the supplied wire-value is one of the four canonical
    /// categories (case-insensitive).
    /// </summary>
    public static bool IsValidCategoryWireValue(string? categoryWireValue)
    {
        var normalized = (categoryWireValue ?? string.Empty).Trim().ToLowerInvariant();
        return AllCategoryWireValues.Contains(normalized);
    }
}
