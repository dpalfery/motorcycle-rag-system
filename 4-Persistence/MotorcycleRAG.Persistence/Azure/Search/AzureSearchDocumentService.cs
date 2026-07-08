using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Entities;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MotorcycleRAG.Contracts.Models.DTOs;


namespace MotorcycleRAG.Persistence.Azure.Search;

/// <summary>
/// Handles Azure AI Search document operations with resilience and correlation tracking.
/// </summary>
/// <remarks>
/// This service backs the legacy full-document indexing path. It resolves a
/// <see cref="SearchClient"/> per operation via <see cref="ISearchClientFactory"/>; in the
/// absence of per-document category context it targets the factory's default category
/// index. The category-aware path is <see cref="ChunkIndexingService"/> (chunks carry
/// their own <c>category</c> and route accordingly).
/// </remarks>
public class AzureSearchDocumentService : IAzureSearchDocumentService
{
    private readonly ISearchClientFactory _clientFactory;
    private readonly ILogger<AzureSearchDocumentService> _logger;
    private readonly IResilienceService _resilienceService;
    private readonly ICorrelationService _correlationService;

    public AzureSearchDocumentService(
        ISearchClientFactory clientFactory,
        ILogger<AzureSearchDocumentService> logger,
        IResilienceService resilienceService,
        ICorrelationService correlationService)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resilienceService = resilienceService ?? throw new ArgumentNullException(nameof(resilienceService));
        _correlationService = correlationService ?? throw new ArgumentNullException(nameof(correlationService));
    }

    public async Task<bool> IndexDocumentsAsync<T>(T[] documents, CancellationToken cancellationToken = default)
    {
        if (documents == null || documents.Length == 0)
            return true;

        var correlationId = _correlationService.GetOrCreateCorrelationId();
        return await _resilienceService.ExecuteAsync<bool>(
            "AzureSearch.IndexDocuments",
            async () => await ExecuteIndexDocumentsAsync(documents, cancellationToken),
            async () => false,
            correlationId,
            cancellationToken);
    }

    public async Task IndexDocumentsAsync(IEnumerable<MotorcycleDocument> documents)
    {
        try
        {
            var documentsArray = documents.ToArray();
            var documentCount = documentsArray.Length;
            _logger.LogDebug("Indexing {DocumentCount} documents", documentCount);

            if (documentCount == 0)
            {
                _logger.LogDebug("No documents to index");
                return;
            }

            var success = await IndexDocumentsAsync(documentsArray);
            if (!success)
            {
                throw new InvalidOperationException($"Failed to index {nameof(documents)}");
            }

            _logger.LogDebug("Successfully indexed {DocumentCount} documents", documentCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error indexing {ParameterName}", nameof(documents));
            throw new InvalidOperationException($"Error indexing {nameof(documents)}", ex);
        }
    }

    public async Task DeleteDocumentsAsync(IEnumerable<string> documentIds)
    {
        try
        {
            var documentIdsArray = documentIds.ToArray();
            var documentCount = documentIdsArray.Length;
            _logger.LogDebug("Deleting {DocumentCount} documents", documentCount);

            if (documentCount == 0)
            {
                _logger.LogDebug("No documents to delete");
                return;
            }

            var correlationId = _correlationService.GetOrCreateCorrelationId();
            await _resilienceService.ExecuteAsync(
                "AzureSearch.DeleteDocuments",
                async () => await ExecuteDeleteDocumentsAsync(documentIdsArray),
                async () => { _logger.LogWarning("Delete documents operation failed"); },
                correlationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting documents");
            throw new InvalidOperationException("Error deleting documents", ex);
        }
    }

    public async Task<bool> CreateOrUpdateIndexAsync(string indexName, CancellationToken cancellationToken = default)
    {
        var correlationId = _correlationService.GetOrCreateCorrelationId();
        return await _resilienceService.ExecuteAsync<bool>(
            "AzureSearch.CreateIndex",
            async () => await ExecuteCreateIndexAsync(indexName, cancellationToken),
            async () => false,
            correlationId,
            cancellationToken);
    }

    private async Task<bool> ExecuteIndexDocumentsAsync<T>(T[] documents, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Indexing {DocumentCount} documents", documents.Length);

        var searchClient = _clientFactory.GetDefaultClient();
        var response = await searchClient.UploadDocumentsAsync(documents, new IndexDocumentsOptions(), cancellationToken);

        var failedCount = response.Value.Results.Count(r => !r.Succeeded);
        if (failedCount > 0)
        {
            _logger.LogWarning("Failed to index {FailedCount} out of {TotalCount} documents",
                failedCount, documents.Length);

            foreach (var failedResult in response.Value.Results.Where(r => !r.Succeeded))
            {
                _logger.LogWarning("Document indexing failed - Key: {Key}, Status: {Status}, Error: {Error}",
                    failedResult.Key, failedResult.Status, failedResult.ErrorMessage);
            }
        }

        var allSucceeded = failedCount == 0;
        _logger.LogDebug("Successfully indexed {DocumentCount} documents", documents.Length);
        return allSucceeded;
    }

    private async Task ExecuteDeleteDocumentsAsync(string[] documentIds)
    {
        var searchClient = _clientFactory.GetDefaultClient();
        var response = await searchClient.DeleteDocumentsAsync("id", documentIds, new IndexDocumentsOptions(), CancellationToken.None);

        var failedCount = response.Value.Results.Count(r => !r.Succeeded);
        if (failedCount > 0)
        {
            _logger.LogWarning("Failed to delete {FailedCount} out of {TotalCount} documents",
                failedCount, documentIds.Length);

            foreach (var result in response.Value.Results.Where(r => !r.Succeeded))
            {
                _logger.LogWarning("Document deletion failed - Key: {Key}, Status: {Status}, Error: {Error}",
                    result.Key, result.Status, result.ErrorMessage);
            }
        }

        _logger.LogDebug("Successfully processed deletion of {DocumentCount} documents", documentIds.Length);
    }

    private Task<bool> ExecuteCreateIndexAsync(string indexName, CancellationToken cancellationToken)
    {
        // Per D1/D4 of the chunk-upload-fix plan, the API must NOT create indexes at runtime.
        // Indexes are provisioned declaratively by Pulumi (see T1). Any caller that reaches
        // this method is a bug: throw loudly instead of silently lying about success.
        _logger.LogError(
            "Runtime index creation is not supported. Index '{IndexName}' must be provisioned by Pulumi IaC.",
            indexName);
        throw new NotSupportedException(
            $"Runtime index creation is not supported; index '{indexName}' must be provisioned by Pulumi IaC. " +
            "Per D1/D4, the API starts but fails the request if the index is missing at runtime.");
    }
}