using Azure;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using AzureSearchModels = Azure.Search.Documents.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Contracts.Models.DTOs;
using DomainSearchResult = MotorcycleRAG.Contracts.Models.DTOs.SearchResult;
using MotorcycleRAG.Domain.Entities;
using Polly;
using AzureSearchOptions = Azure.Search.Documents.SearchOptions;
using MotorcycleRAG.Persistence.Azure.Search;

namespace MotorcycleRAG.Persistence.Azure;

/// <summary>
/// Azure AI Search client wrapper with connection management and resilience
/// S1200: This class requires multiple dependencies for comprehensive Azure Search functionality
/// including resilience, correlation tracking, and configuration management.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "SearchClient is not IDisposable")]
public class AzureSearchClientWrapper : IAzureSearchClient, IDisposable {
    private readonly IAzureSearchQueryService _queryService;
    private readonly IAzureSearchDocumentService _documentService;
    private readonly IAzureSearchHealthService _healthService;

    public AzureSearchClientWrapper(
        IOptions<AzureFoundryOptions> azureConfig,
        IOptions<Core.Options.SearchOptions> searchConfig,
        ILogger<AzureSearchClientWrapper> logger,
        IAzureSearchQueryService queryService,
        IAzureSearchDocumentService documentService,
        IAzureSearchHealthService healthService) {
        ArgumentNullException.ThrowIfNull(azureConfig);
        ArgumentNullException.ThrowIfNull(searchConfig);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(queryService);
        ArgumentNullException.ThrowIfNull(documentService);
        ArgumentNullException.ThrowIfNull(healthService);

        var azureConfigValue = azureConfig.Value ?? throw new ArgumentNullException(nameof(azureConfig));
        // searchConfig is retained for DI/forward-compat but the wrapper no longer binds to a single index.
        _ = searchConfig.Value ?? throw new ArgumentNullException(nameof(searchConfig));
        _queryService = queryService;
        _documentService = documentService;
        _healthService = healthService;

        logger.LogInformation("Azure Search client initialized with endpoint: {Endpoint} across {CategoryCount} category-partitioned indexes (dirt/touring/sport/cruiser)",
            azureConfigValue.SearchServiceEndpoint, 4);
    }

    public Task<DomainSearchResult[]> SearchAsync(string searchText)
    {
        return SearchAsync(searchText, 50, CancellationToken.None);
    }

    public Task<DomainSearchResult[]> SearchAsync(string searchText, int maxResults)
    {
        return SearchAsync(searchText, maxResults, CancellationToken.None);
    }

    public async Task<DomainSearchResult[]> SearchAsync(
        string searchText,
        int maxResults,
        CancellationToken cancellationToken) {
        return await _healthService.SearchAsync(searchText, maxResults, cancellationToken);
    }

    public Task<bool> IndexDocumentsAsync<T>(T[] documents)
    {
        return IndexDocumentsAsync(documents, CancellationToken.None);
    }

    public async Task<bool> IndexDocumentsAsync<T>(
        T[] documents,
        CancellationToken cancellationToken) {
        return await _documentService.IndexDocumentsAsync(documents, cancellationToken);
    }

    public Task<bool> CreateOrUpdateIndexAsync(string indexName)
    {
        return CreateOrUpdateIndexAsync(indexName, CancellationToken.None);
    }

    public async Task<bool> CreateOrUpdateIndexAsync(
        string indexName,
        CancellationToken cancellationToken) {
        return await _documentService.CreateOrUpdateIndexAsync(indexName, cancellationToken);
    }

    public Task<bool> IsHealthyAsync()
    {
        return IsHealthyAsync(CancellationToken.None);
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken) {
        return await _healthService.IsHealthyAsync(cancellationToken);
    }

    private bool _disposed;

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        // SearchClient is not IDisposable; nothing to dispose.
        _disposed = true;
    }

    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    // Implement VectorSearchAsync
    public async Task<DomainSearchResult[]> VectorSearchAsync(string query, MotorcycleRAG.Core.Options.SearchOptions options) {
        return await _queryService.VectorSearchAsync(query, options);
    }

    public async Task<DomainSearchResult[]> HybridSearchAsync(string query, MotorcycleRAG.Core.Options.SearchOptions options) {
        return await _queryService.HybridSearchAsync(query, options);
    }

    public async Task<DomainSearchResult[]> SearchAsync(string query, MotorcycleRAG.Core.Options.SearchOptions options) {
        return await _queryService.SearchAsync(query, options);
    }

    public async Task IndexDocumentsAsync(IEnumerable<MotorcycleDocument> documents) {
        await _documentService.IndexDocumentsAsync(documents);
    }

    public async Task DeleteDocumentsAsync(IEnumerable<string> documentIds) {
        await _documentService.DeleteDocumentsAsync(documentIds);
    }

}
