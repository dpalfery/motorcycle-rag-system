using MotorcycleRAG.Domain.Entities;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Interface for Azure AI Search document operations
/// </summary>
public interface IAzureSearchDocumentService
{
    /// <summary>
    /// Indexes documents
    /// </summary>
    Task<bool> IndexDocumentsAsync<T>(T[] documents, CancellationToken cancellationToken = default);

    /// <summary>
    /// Indexes documents (convenience method)
    /// </summary>
    Task IndexDocumentsAsync(IEnumerable<MotorcycleDocument> documents);

    /// <summary>
    /// Deletes documents from index
    /// </summary>
    Task DeleteDocumentsAsync(IEnumerable<string> documentIds);

    /// <summary>
    /// Creates or updates index
    /// </summary>
    Task<bool> CreateOrUpdateIndexAsync(string indexName, CancellationToken cancellationToken = default);
}