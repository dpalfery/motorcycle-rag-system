using Azure;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Domain.DTOs;
using MotorcycleRAG.Domain.Entities;
using System.Diagnostics;

namespace MotorcycleRAG.Persistence.Search;

/// <summary>
/// Service for indexing motorcycle documents to Azure AI Search
/// </summary>
public class MotorcycleIndexingService : IMotorcycleIndexingService
{
    private readonly IAzureSearchClient _searchClient;
    private readonly SearchIndexClient _indexClient;
    private readonly ILogger<MotorcycleIndexingService> _logger;
    private readonly SearchOptions _searchOptions;

    public MotorcycleIndexingService(
        IAzureSearchClient searchClient,
        SearchIndexClient indexClient,
        IOptions<SearchOptions> searchOptions,
        ILogger<MotorcycleIndexingService> logger)
    {
        _searchClient = searchClient ?? throw new ArgumentNullException(nameof(searchClient));
        _indexClient = indexClient ?? throw new ArgumentNullException(nameof(indexClient));
        _searchOptions = searchOptions?.Value ?? throw new ArgumentNullException(nameof(searchOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<BatchIndexingResult> IndexDocumentsAsync(IEnumerable<MotorcycleDocument> documents)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new BatchIndexingResult
        {
            IndexName = _searchOptions.IndexName
        };

        try
        {
            var documentList = documents.ToList();
            result.DocumentsProcessed = documentList.Count;

            _logger.LogInformation("Starting batch indexing of {DocumentCount} documents to index {IndexName}",
                documentList.Count, _searchOptions.IndexName);

            // T055: Locator metadata is now populated on MotorcycleDocument directly by PDF processor
            // Process in batches based on configured batch size
            var batches = documentList
                .Select((doc, index) => new { doc, index })
                .GroupBy(x => x.index / _searchOptions.BatchSize)
                .Select(g => g.Select(x => x.doc).ToList());

            foreach (var batch in batches)
            {
                try
                {
                    await _searchClient.IndexDocumentsAsync(batch);
                    result.DocumentsIndexed += batch.Count;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error indexing batch of {BatchSize} documents", batch.Count);
                    result.Errors.Add($"Batch indexing error: {ex.Message}");
                }
            }

            stopwatch.Stop();
            result.ProcessingTime = stopwatch.Elapsed;
            result.Success = result.Errors.Count == 0;
            result.Message = result.Success
                ? $"Successfully indexed {result.DocumentsIndexed} documents"
                : $"Indexed {result.DocumentsIndexed} of {result.DocumentsProcessed} documents with {result.Errors.Count} errors";

            _logger.LogInformation("Batch indexing completed: {Message} in {ElapsedMs}ms",
                result.Message, stopwatch.ElapsedMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Failed to index documents to {IndexName}", _searchOptions.IndexName);

            result.Success = false;
            result.Message = $"Indexing failed: {ex.Message}";
            result.Errors.Add(ex.Message);
            result.ProcessingTime = stopwatch.Elapsed;

            return result;
        }
    }

    /// <inheritdoc />
    public async Task<IndexingStatistics> GetIndexingStatisticsAsync()
    {
        var statistics = new IndexingStatistics();

        try
        {
            _logger.LogDebug("Retrieving indexing statistics");

            var indexNames = _indexClient.GetIndexNamesAsync();
            
            await foreach (var indexName in indexNames)
            {
                try
                {
                    var index = await _indexClient.GetIndexAsync(indexName);
                    var indexInfo = new IndexInfo
                    {
                        Name = indexName,
                        IsHealthy = true
                    };
                    statistics.Indexes.Add(indexInfo);
                    statistics.HealthyIndexes++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get info for index {IndexName}", indexName);
                    statistics.Indexes.Add(new IndexInfo
                    {
                        Name = indexName,
                        IsHealthy = false
                    });
                }
            }

            statistics.TotalDocuments = statistics.Indexes.Sum(i => i.DocumentCount);
            statistics.TotalStorageSize = statistics.Indexes.Sum(i => i.StorageSize);

            _logger.LogInformation("Retrieved statistics for {IndexCount} indexes with {TotalDocs} total documents",
                statistics.Indexes.Count, statistics.TotalDocuments);

            return statistics;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve indexing statistics");
            statistics.ErrorMessage = ex.Message;
            return statistics;
        }
    }

    /// <inheritdoc />
    public async Task<IndexCreationResult> RebuildIndexAsync()
    {
        var result = new IndexCreationResult();

        try
        {
            _logger.LogInformation("Starting index rebuild for {IndexName}", _searchOptions.IndexName);

            // Delete existing index if it exists
            try
            {
                await _indexClient.DeleteIndexAsync(_searchOptions.IndexName);
                _logger.LogInformation("Deleted existing index {IndexName}", _searchOptions.IndexName);
            }
            catch (global::Azure.RequestFailedException ex) when (ex.Status == 404)
            {
                _logger.LogDebug("Index {IndexName} does not exist, will create new", _searchOptions.IndexName);
            }

            // Create new index with schema including locator fields
            var indexDefinition = CreateMotorcycleDocumentIndexDefinition();
            await _indexClient.CreateIndexAsync(indexDefinition);
            
            result.CreatedIndexes.Add(_searchOptions.IndexName);
            result.Success = true;
            result.Message = $"Successfully rebuilt index {_searchOptions.IndexName} with locator fields";

            _logger.LogInformation("Index rebuild completed: {Message}", result.Message);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rebuild index {IndexName}", _searchOptions.IndexName);

            result.Success = false;
            result.Message = $"Index rebuild failed: {ex.Message}";
            result.Errors.Add(ex.Message);

            return result;
        }
    }

    /// <summary>
    /// Creates the Azure AI Search index schema for MotorcycleDocument including locator fields
    /// </summary>
    private SearchIndex CreateMotorcycleDocumentIndexDefinition()
    {
        var indexName = _searchOptions.IndexName;
        
        var index = new SearchIndex(indexName)
        {
            Fields = new List<SearchField>
            {
                // Key field
                new SimpleField("id", SearchFieldDataType.String) { IsKey = true, IsFilterable = true },
                
                // Core document fields
                new SearchField("title", SearchFieldDataType.String) { IsSearchable = true, IsFilterable = true, IsSortable = true },
                new SearchField("content", SearchFieldDataType.String) { IsSearchable = true },
                new SimpleField("documentType", SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
                
                // Motorcycle specification fields
                new SearchField("make", SearchFieldDataType.String) { IsSearchable = true, IsFilterable = true, IsFacetable = true },
                new SearchField("model", SearchFieldDataType.String) { IsSearchable = true, IsFilterable = true, IsFacetable = true },
                new SimpleField("year", SearchFieldDataType.Int32) { IsFilterable = true, IsSortable = true, IsFacetable = true },
                
                // Source metadata fields (from DocumentMetadata)
                new SearchField("sourceFile", SearchFieldDataType.String) { IsSearchable = true, IsFilterable = true },
                new SearchField("sourceUrl", SearchFieldDataType.String) { IsSearchable = true, IsFilterable = true },
                new SearchField("author", SearchFieldDataType.String) { IsSearchable = true, IsFilterable = true },
                new SimpleField("publishedDate", SearchFieldDataType.DateTimeOffset) { IsFilterable = true, IsSortable = true },
                
                // Section field (from DocumentMetadata - kept for backward compatibility)
                new SearchField("section", SearchFieldDataType.String) { IsSearchable = true, IsFilterable = true },
                
                // T055: Locator metadata fields for citation support
                new SimpleField("pageNumber", SearchFieldDataType.Int32) { IsFilterable = true, IsSortable = true },
                new SearchField("pageRange", SearchFieldDataType.String) { IsSearchable = true, IsFilterable = true },
                new SearchField("primarySection", SearchFieldDataType.String) { IsSearchable = true, IsFilterable = true },
                new SimpleField("sectionLevel", SearchFieldDataType.Int32) { IsFilterable = true, IsSortable = true },
                new SearchField("sectionHeadings", SearchFieldDataType.Collection(SearchFieldDataType.String)) { IsSearchable = true, IsFilterable = true },
                new SearchField("tableCaption", SearchFieldDataType.String) { IsSearchable = true, IsFilterable = true },
                new SimpleField("chunkIndex", SearchFieldDataType.Int32) { IsFilterable = true, IsSortable = true },
                
                // Timestamp fields
                new SimpleField("createdAt", SearchFieldDataType.DateTimeOffset) { IsFilterable = true, IsSortable = true },
                new SimpleField("updatedAt", SearchFieldDataType.DateTimeOffset) { IsFilterable = true, IsSortable = true },
                
                // Tags field (from DocumentMetadata)
                new SearchField("tags", SearchFieldDataType.Collection(SearchFieldDataType.String)) { IsSearchable = true, IsFilterable = true, IsFacetable = true },
                
                // Vector field for semantic search
                new SearchField("contentVector", SearchFieldDataType.Collection(SearchFieldDataType.Single))
                {
                    IsSearchable = true,
                    IsHidden = false,
                    VectorSearchDimensions = 1536, // OpenAI text-embedding-3-large dimensions
                    VectorSearchProfileName = "vector-config"
                }
            },
            VectorSearch = new VectorSearch
            {
                Algorithms =
                {
                    new HnswAlgorithmConfiguration("vector-algo")
                    {
                        Parameters = new HnswParameters
                        {
                            M = 4,
                            EfConstruction = 400,
                            EfSearch = 500
                        }
                    }
                },
                Profiles =
                {
                    new VectorSearchProfile("vector-config", "vector-algo")
                }
            }
        };

        _logger.LogDebug("Created index schema for {IndexName} with {FieldCount} fields including locator fields",
            indexName, index.Fields.Count);

        return index;
    }
}
