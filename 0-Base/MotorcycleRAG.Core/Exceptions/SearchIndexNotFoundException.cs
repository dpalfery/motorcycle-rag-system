namespace MotorcycleRAG.Core.Exceptions;

/// <summary>
/// Thrown when an Azure AI Search index required for chunk indexing does not exist.
/// </summary>
/// <remarks>
/// Per decisions D1/D4 in the chunk-upload fix plan, indexes are provisioned by Pulumi
/// infrastructure-as-code and the API must <b>not</b> create them at runtime. This
/// exception surfaces a missing-index condition loudly (task T6) instead of silently
/// swallowing the resulting HTTP 404. It is raised by the index-existence precheck in
/// <c>ChunkIndexingService</c> before any batch is uploaded, and re-thrown by the batch
/// indexer when the Search service itself returns a 404/400/401.
/// </remarks>
public sealed class SearchIndexNotFoundException : Exception
{
    /// <summary>
    /// Gets the name of the Azure AI Search index that was not found.
    /// </summary>
    public string IndexName { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchIndexNotFoundException"/> class.
    /// </summary>
    /// <param name="indexName">The name of the missing index.</param>
    public SearchIndexNotFoundException(string indexName)
        : base(BuildMessage(indexName))
    {
        IndexName = indexName ?? string.Empty;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchIndexNotFoundException"/> class
    /// with a reference to the inner exception that caused this condition (typically a 404
    /// <c>RequestFailedException</c> from the Azure SDK).
    /// </summary>
    /// <param name="indexName">The name of the missing index.</param>
    /// <param name="innerException">The exception that is the cause of this condition.</param>
    public SearchIndexNotFoundException(string indexName, Exception innerException)
        : base(BuildMessage(indexName), innerException)
    {
        IndexName = indexName ?? string.Empty;
    }

    private static string BuildMessage(string? indexName)
        => $"Azure AI Search index '{indexName ?? "<null>"}' does not exist. "
           + "Indexes are provisioned by Pulumi IaC (D4); the API does not create indexes (D1).";
}
