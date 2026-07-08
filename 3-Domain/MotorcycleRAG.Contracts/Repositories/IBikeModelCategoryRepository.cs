namespace MotorcycleRAG.Contracts.Repositories;

/// <summary>
/// Authoritative (Make, Model) -&gt; Category cache for the D7 motorcycle category classifier.
/// </summary>
/// <remarks>
/// &lt;para&gt;This repository is the deterministic read path for classifier cache lookups: a single
/// equality seek on the unique index <c>UQ_BikeModelCategory_Make_Model</c>. It deliberately does
/// NOT use SQL Server Graph traversal or LIKE matching (resolves the R5 make-vs-name impedance).&lt;/para&gt;
/// &lt;para&gt;&lt;b>Collation / casing contract:</b&gt; callers MUST normalize <paramref name="make"/> and
/// <paramref name="model"/> via <c>BikeModel.NormalizeName</c> before calling so that
/// "honda CBR1000RR" and "Honda CBR1000RR" resolve to the same row regardless of database collation.
/// The <paramref name="category"/> argument is case-insensitive; the repository maps it to the
/// storage form required by the <c>BikeModelCategory</c> CHECK constraint.&lt;/para&gt;
/// &lt;para&gt;Year is intentionally NOT part of the key — a (Make, Model) has exactly one Category.&lt;/para&gt;
/// </remarks>
public interface IBikeModelCategoryRepository
{
    /// <summary>
    /// Returns the cached category for the given (Make, Model), or <c>null</c> on cache miss.
    /// </summary>
    /// <param name="make">Normalized manufacturer name (e.g. "Honda").</param>
    /// <param name="model">Normalized model designation (e.g. "CBR 1000RR").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored category token, or <c>null</c> if no row matches.</returns>
    /// <remarks>
    /// Implementations must use a parameterized equality seek — no <c>LIKE</c>, no graph traversal.
    /// </remarks>
    Task<string?> GetCategoryAsync(
        string make,
        string model,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts or updates the cached category for the given (Make, Model) using a MERGE upsert.
    /// </summary>
    /// <param name="make">Normalized manufacturer name.</param>
    /// <param name="model">Normalized model designation.</param>
    /// <param name="category">
    /// Category token — case-insensitive, one of: Dirt, Touring, Sport, Cruiser.
    /// The repository maps it to the storage form required by the table's CHECK constraint.
    /// </param>
    /// <param name="source">
    /// Provenance tag (default "Classifier"). Distinguishes LLM-derived ("Classifier"),
    /// deterministic ("CSV"), and operator ("Manual") classifications for audit/retraining.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpsertAsync(
        string make,
        string model,
        string category,
        string source = "Classifier",
        CancellationToken cancellationToken = default);
}
