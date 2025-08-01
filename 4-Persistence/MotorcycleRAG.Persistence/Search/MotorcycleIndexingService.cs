using Azure;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Core.Interfaces;
using MotorcycleRAG.Core.Models;
using System.Text.Json;

namespace MotorcycleRAG.Infrastructure.Search;

/// <summary>
/// Azure AI Search indexing service with hybrid vector/keyword capabilities
/// Handles batch indexing for CSV and PDF processed data with metadata management
/// </summary>
public class MotorcycleIndexingService : IMotorcycleIndexingService
{
    private readonly SearchIndexClient _indexClient;
    private readonly IAzureSearchClient _searchClient;
    private readonly SearchConfiguration _searchConfig;
    private readonly ILogger<MotorcycleIndexingService> _logger;
    private readonly SemaphoreSlim _indexingSemaphore;

    // Index schema constants
    private const string CSV_INDEX_NAME = "motorcycle-csv-index";
    private const string PDF_INDEX_NAME = "motorcycle-pdf-index";
    private const string UNIFIED_INDEX_NAME = "motorcycle-unified-index";

    public MotorcycleIndexingService(
        SearchIndexClient indexClient,
        IAzureSearchClient searchClient,
        IOptions<SearchConfiguration> searchConfig,
        ILogger<MotorcycleIndexingService> logger)
    {
        _indexClient = indexClient ?? throw new ArgumentNullException(nameof(indexClient));
        _searchClient = searchClient ?? throw new ArgumentNullException(nameof(searchClient));
        _searchConfig = searchConfig?.Value ?? throw new ArgumentNullException(nameof(searchConfig));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // Limit concurrent indexing operations to prevent overwhelming the service
        _indexingSemaphore = new SemaphoreSlim(3, 3);
    }

    /// <summary>
    /// Create or update search indexes with hybrid vector/keyword capabilities
    /// </summary>
    public async Task<IndexCreationResult> CreateSearchIndexesAsync(CancellationToken cancellationToken = default)
    {
        var result = new IndexCreationResult();
        var createdIndexes = new List<string>();
        var errors = new List<string>();

        try
        {
            _logger.LogInformation("Creating search indexes with hybrid capabilities");

            // Create CSV specifications index
            var csvIndexResult = await CreateCSVIndexAsync(cancellationToken);
            if (csvIndexResult.Success)
            {
                createdIndexes.Add(CSV_INDEX_NAME);
            }
            else
            {
                errors.AddRange(csvIndexResult.Errors);
            }

            // Create PDF manuals index
            var pdfIndexResult = await CreatePDFIndexAsync(cancellationToken);
            if (pdfIndexResult.Success)
            {
                createdIndexes.Add(PDF_INDEX_NAME);
            }
            else
            {
                errors.AddRange(pdfIndexResult.Errors);
            }

            // Create unified index for cross-source search
            var unifiedIndexResult = await CreateUnifiedIndexAsync(cancellationToken);
            if (unifiedIndexResult.Success)
            {
                createdIndexes.Add(UNIFIED_INDEX_NAME);
            }
            else
            {
                errors.AddRange(unifiedIndexResult.Errors);
            }

            result.Success = createdIndexes.Count > 0;
            result.CreatedIndexes = createdIndexes;
            result.Errors = errors;
            result.Message = result.Success 
                ? $"Successfully created {createdIndexes.Count} indexes"
                : "Failed to create any indexes";

            _logger.LogInformation("Index creation completed. Created: {CreatedCount}, Errors: {ErrorCount}",
                createdIndexes.Count, errors.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error during index creation");
            result.Success = false;
            result.Message = $"Fatal error: {ex.Message}";
            result.Errors.Add(ex.Message);
        }

        return result;
    }

    /// <summary>
    /// Index CSV processed data with batch processing (100-1000 documents per batch)
    /// </summary>
    public async Task<IndexingResult> IndexCSVDataAsync(
        ProcessedData processedData, 
        CancellationToken cancellationToken = default)
    {
        await _indexingSemaphore.WaitAsync(cancellationToken);
        
        try
        {
            _logger.LogInformation("Starting CSV data indexing for {DocumentCount} documents", 
                processedData.Documents.Count);

            var result = await IndexDocumentsBatchAsync(
                processedData.Documents,
                CSV_INDEX_NAME,
                DocumentType.Specification,
                cancellationToken);

            // Also index in unified index for cross-source search
            var unifiedResult = await IndexDocumentsBatchAsync(
                processedData.Documents,
                UNIFIED_INDEX_NAME,
                DocumentType.Specification,
                cancellationToken);

            // Combine results
            result.DocumentsIndexed += unifiedResult.DocumentsIndexed;
            result.Errors.AddRange(unifiedResult.Errors);
            result.Success = result.Success && unifiedResult.Success;

            return result;
        }
        finally
        {
            _indexingSemaphore.Release();
        }
    }

    /// <summary>
    /// Index PDF processed data with batch processing
    /// </summary>
    public async Task<IndexingResult> IndexPDFDataAsync(
        ProcessedData processedData, 
        CancellationToken cancellationToken = default)
    {
        await _indexingSemaphore.WaitAsync(cancellationToken);
        
        try
        {
            _logger.LogInformation("Starting PDF data indexing for {DocumentCount} documents", 
                processedData.Documents.Count);

            var result = await IndexDocumentsBatchAsync(
                processedData.Documents,
                PDF_INDEX_NAME,
                DocumentType.Manual,
                cancellationToken);

            // Also index in unified index for cross-source search
            var unifiedResult = await IndexDocumentsBatchAsync(
                processedData.Documents,
                UNIFIED_INDEX_NAME,
                DocumentType.Manual,
                cancellationToken);

            // Combine results
            result.DocumentsIndexed += unifiedResult.DocumentsIndexed;
            result.Errors.AddRange(unifiedResult.Errors);
            result.Success = result.Success && unifiedResult.Success;

            return result;
        }
        finally
        {
            _indexingSemaphore.Release();
        }
    }

    /// <summary>
    /// Get indexing statistics and health information
    /// </summary>
    public async Task<IndexingStatistics> GetIndexingStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var statistics = new IndexingStatistics();

        try
        {
            var indexes = new[] { CSV_INDEX_NAME, PDF_INDEX_NAME, UNIFIED_INDEX_NAME };
            
            foreach (var indexName in indexes)
            {
                try
                {
                    var indexStats = await _indexClient.GetIndexStatisticsAsync(indexName, cancellationToken);
                    var indexInfo = new IndexInfo
                    {
                        Name = indexName,
                        DocumentCount = indexStats.Value.DocumentCount,
                        StorageSize = indexStats.Value.StorageSize,
                        IsHealthy = true
                    };
                    statistics.Indexes.Add(indexInfo);
                }
                catch (RequestFailedException ex) when (ex.Status == 404)
                {
                    _logger.LogWarning("Index {IndexName} not found", indexName);
                    statistics.Indexes.Add(new IndexInfo
                    {
                        Name = indexName,
                        DocumentCount = 0,
                        StorageSize = 0,
                        IsHealthy = false,
                        ErrorMessage = "Index not found"
                    });
                }
            }

            statistics.TotalDocuments = statistics.Indexes.Sum(i => i.DocumentCount);
            statistics.TotalStorageSize = statistics.Indexes.Sum(i => i.StorageSize);
            statistics.HealthyIndexes = statistics.Indexes.Count(i => i.IsHealthy);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving indexing statistics");
            statistics.ErrorMessage = ex.Message;
        }

        return statistics;
    }

    /// <summary>
    /// Delete and recreate all indexes (for maintenance/reset scenarios)
    /// </summary>
    public async Task<bool> ResetIndexesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogWarning("Resetting all search indexes - this will delete existing data");

            var indexes = new[] { CSV_INDEX_NAME, PDF_INDEX_NAME, UNIFIED_INDEX_NAME };
            var deletedCount = 0;

            // Delete existing indexes
            foreach (var indexName in indexes)
            {
                try
                {
                    await _indexClient.DeleteIndexAsync(indexName, cancellationToken);
                    deletedCount++;
                    _logger.LogInformation("Deleted index: {IndexName}", indexName);
                }
                catch (RequestFailedException ex) when (ex.Status == 404)
                {
                    _logger.LogInformation("Index {IndexName} not found, skipping deletion", indexName);
                }
            }

            // Recreate indexes
            var creationResult = await CreateSearchIndexesAsync(cancellationToken);
            
            _logger.LogInformation("Index reset completed. Deleted: {DeletedCount}, Created: {CreatedCount}",
                deletedCount, creationResult.CreatedIndexes.Count);

            return creationResult.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting indexes");
            return false;
        }
    }

    #region Private Methods

    private async Task<IndexCreationResult> CreateCSVIndexAsync(CancellationToken cancellationToken)
    {
        var result = new IndexCreationResult();

        try
        {
            var index = new SearchIndex(CSV_INDEX_NAME)
            {
                Fields =
                {
                    // Core document fields
                    new SimpleField("id", SearchFieldDataType.String) { IsKey = true, IsFilterable = true },
                    new SearchableField("title") { IsFilterable = true, IsSortable = true },
                    new SearchableField("content") { AnalyzerName = LexicalAnalyzerName.EnMicrosoft },
                    
                    // Vector field for semantic search
                    new VectorSearchField("contentVector", 1536, "default-vector-profile"),
                    
                    // Motorcycle-specific fields for CSV data
                    new SimpleField("documentType", SearchFieldDataType.String) { IsFilterable = true },
                    new SearchableField("make") { IsFilterable = true, IsFacetable = true },
                    new SearchableField("model") { IsFilterable = true, IsFacetable = true },
                    new SimpleField("year", SearchFieldDataType.Int32) { IsFilterable = true, IsFacetable = true, IsSortable = true },
                    
                    // Metadata fields
                    new SimpleField("sourceFile", SearchFieldDataType.String) { IsFilterable = true },
                    new SimpleField("section", SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
                    new SimpleField("createdAt", SearchFieldDataType.DateTimeOffset) { IsFilterable = true, IsSortable = true },
                    new SimpleField("updatedAt", SearchFieldDataType.DateTimeOffset) { IsFilterable = true, IsSortable = true },
                    
                    // Additional searchable metadata
                    new SearchableField("tags") { IsFilterable = true, IsFacetable = true },
                    new SimpleField("metadata", SearchFieldDataType.String) { IsFilterable = false }
                },
                
                VectorSearch = CreateVectorSearchConfiguration(),
                SemanticSearch = CreateSemanticSearchConfiguration("csv-semantic-config"),
                ScoringProfiles =
                {
                    CreateScoringProfile("csv-relevance-profile")
                }
            };

            await _indexClient.CreateOrUpdateIndexAsync(index, cancellationToken: cancellationToken);
            
            result.Success = true;
            result.CreatedIndexes.Add(CSV_INDEX_NAME);
            
            _logger.LogInformation("Successfully created CSV index: {IndexName}", CSV_INDEX_NAME);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create CSV index");
            result.Success = false;
            result.Errors.Add($"CSV index creation failed: {ex.Message}");
        }

        return result;
    }

    private async Task<IndexCreationResult> CreatePDFIndexAsync(CancellationToken cancellationToken)
    {
        var result = new IndexCreationResult();

        try
        {
            var index = new SearchIndex(PDF_INDEX_NAME)
            {
                Fields =
                {
                    // Core document fields
                    new SimpleField("id", SearchFieldDataType.String) { IsKey = true, IsFilterable = true },
                    new SearchableField("title") { IsFilterable = true, IsSortable = true },
                    new SearchableField("content") { AnalyzerName = LexicalAnalyzerName.EnMicrosoft },
                    
                    // Vector field for semantic search
                    new VectorSearchField("contentVector", 1536, "default-vector-profile"),
                    
                    // PDF-specific fields
                    new SimpleField("documentType", SearchFieldDataType.String) { IsFilterable = true },
                    new SearchableField("make") { IsFilterable = true, IsFacetable = true },
                    new SearchableField("model") { IsFilterable = true, IsFacetable = true },
                    new SimpleField("year", SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
                    
                    // Document structure fields
                    new SimpleField("pageNumber", SearchFieldDataType.Int32) { IsFilterable = true, IsSortable = true },
                    new SimpleField("section", SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
                    new SimpleField("chunkType", SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
                    
                    // Metadata fields
                    new SimpleField("sourceFile", SearchFieldDataType.String) { IsFilterable = true },
                    new SimpleField("sourceUrl", SearchFieldDataType.String) { IsFilterable = true },
                    new SimpleField("author", SearchFieldDataType.String) { IsFilterable = true },
                    new SimpleField("publishedDate", SearchFieldDataType.DateTimeOffset) { IsFilterable = true, IsSortable = true },
                    new SimpleField("createdAt", SearchFieldDataType.DateTimeOffset) { IsFilterable = true, IsSortable = true },
                    new SimpleField("updatedAt", SearchFieldDataType.DateTimeOffset) { IsFilterable = true, IsSortable = true },
                    
                    // Additional searchable metadata
                    new SearchableField("tags") { IsFilterable = true, IsFacetable = true },
                    new SimpleField("language", SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
                    new SimpleField("metadata", SearchFieldDataType.String) { IsFilterable = false }
                },
                
                VectorSearch = CreateVectorSearchConfiguration(),
                SemanticSearch = CreateSemanticSearchConfiguration("pdf-semantic-config"),
                ScoringProfiles =
                {
                    CreateScoringProfile("pdf-relevance-profile")
                }
            };

            await _indexClient.CreateOrUpdateIndexAsync(index, cancellationToken: cancellationToken);
            
            result.Success = true;
            result.CreatedIndexes.Add(PDF_INDEX_NAME);
            
            _logger.LogInformation("Successfully created PDF index: {IndexName}", PDF_INDEX_NAME);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create PDF index");
            result.Success = false;
            result.Errors.Add($"PDF index creation failed: {ex.Message}");
        }

        return result;
    }

    private async Task<IndexCreationResult> CreateUnifiedIndexAsync(CancellationToken cancellationToken)
    {
        var result = new IndexCreationResult();

        try
        {
            var index = new SearchIndex(UNIFIED_INDEX_NAME)
            {
                Fields =
                {
                    // Core document fields
                    new SimpleField("id", SearchFieldDataType.String) { IsKey = true, IsFilterable = true },
                    new SearchableField("title") { IsFilterable = true, IsSortable = true },
                    new SearchableField("content") { AnalyzerName = LexicalAnalyzerName.EnMicrosoft },
                    
                    // Vector field for semantic search
                    new VectorSearchField("contentVector", 1536, "default-vector-profile"),
                    
                    // Universal motorcycle fields
                    new SimpleField("documentType", SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
                    new SearchableField("make") { IsFilterable = true, IsFacetable = true },
                    new SearchableField("model") { IsFilterable = true, IsFacetable = true },
                    new SimpleField("year", SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
                    
                    // Source identification
                    new SimpleField("sourceType", SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
                    new SimpleField("sourceFile", SearchFieldDataType.String) { IsFilterable = true },
                    new SimpleField("sourceUrl", SearchFieldDataType.String) { IsFilterable = true },
                    
                    // Document structure (optional for CSV)
                    new SimpleField("pageNumber", SearchFieldDataType.Int32) { IsFilterable = true, IsSortable = true },
                    new SimpleField("section", SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
                    
                    // Metadata fields
                    new SimpleField("author", SearchFieldDataType.String) { IsFilterable = true },
                    new SimpleField("publishedDate", SearchFieldDataType.DateTimeOffset) { IsFilterable = true, IsSortable = true },
                    new SimpleField("createdAt", SearchFieldDataType.DateTimeOffset) { IsFilterable = true, IsSortable = true },
                    new SimpleField("updatedAt", SearchFieldDataType.DateTimeOffset) { IsFilterable = true, IsSortable = true },
                    
                    // Additional searchable metadata
                    new SearchableField("tags") { IsFilterable = true, IsFacetable = true },
                    new SimpleField("language", SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
                    new SimpleField("metadata", SearchFieldDataType.String) { IsFilterable = false }
                },
                
                VectorSearch = CreateVectorSearchConfiguration(),
                SemanticSearch = CreateSemanticSearchConfiguration("unified-semantic-config"),
                ScoringProfiles =
                {
                    CreateScoringProfile("unified-relevance-profile")
                }
            };

            await _indexClient.CreateOrUpdateIndexAsync(index, cancellationToken: cancellationToken);
            
            result.Success = true;
            result.CreatedIndexes.Add(UNIFIED_INDEX_NAME);
            
            _logger.LogInformation("Successfully created unified index: {IndexName}", UNIFIED_INDEX_NAME);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create unified index");
            result.Success = false;
            result.Errors.Add($"Unified index creation failed: {ex.Message}");
        }

        return result;
    }   
 private VectorSearch CreateVectorSearchConfiguration()
    {
        return new VectorSearch
        {
            Profiles =
            {
                new VectorSearchProfile("default-vector-profile", "default-algorithm-config")
            },
            Algorithms =
            {
                new HnswAlgorithmConfiguration("default-algorithm-config")
                {
                    Parameters = new HnswParameters
                    {
                        Metric = VectorSearchAlgorithmMetric.Cosine,
                        M = 4,
                        EfConstruction = 400,
                        EfSearch = 500
                    }
                }
            }
        };
    }

    private SemanticSearch CreateSemanticSearchConfiguration(string configName)
    {
        return new SemanticSearch
        {
            Configurations =
            {
                new SemanticConfiguration(configName, new SemanticPrioritizedFields
                {
                    TitleField = new SemanticField("title"),
                    ContentFields =
                    {
                        new SemanticField("content")
                    },
                    KeywordsFields =
                    {
                        new SemanticField("tags"),
                        new SemanticField("make"),
                        new SemanticField("model")
                    }
                })
            }
        };
    }

    private ScoringProfile CreateScoringProfile(string profileName)
    {
        return new ScoringProfile(profileName)
        {
            TextWeights = new TextWeights(new Dictionary<string, double>
            {
                ["title"] = 2.0,
                ["content"] = 1.0,
                ["make"] = 1.5,
                ["model"] = 1.5,
                ["tags"] = 1.2
            }),
            Functions =
            {
                new FreshnessScoringFunction("updatedAt", 2.0, new FreshnessScoringParameters(TimeSpan.FromDays(365))
                {
                    BoostingDuration = TimeSpan.FromDays(30)
                })
            }
        };
    }

    private async Task<IndexingResult> IndexDocumentsBatchAsync(
        List<MotorcycleDocument> documents,
        string indexName,
        DocumentType expectedType,
        CancellationToken cancellationToken)
    {
        var result = new IndexingResult
        {
            IndexName = indexName
        };
        var startTime = DateTime.UtcNow;

        try
        {
            // Filter documents by expected type
            var filteredDocuments = documents.Where(d => d.Type == expectedType).ToList();
            
            if (filteredDocuments.Count == 0)
            {
                result.Success = true;
                result.Message = $"No documents of type {expectedType} to index";
                return result;
            }

            _logger.LogDebug("Indexing {DocumentCount} documents to {IndexName}", 
                filteredDocuments.Count, indexName);

            // Process documents in batches (100-1000 per batch as per requirements)
            var batchSize = ValidateBatchSize(_searchConfig.BatchSize, filteredDocuments.Count);
            var batches = filteredDocuments.Chunk(batchSize);
            var totalIndexed = 0;
            var errors = new List<string>();
            var batchNumber = 0;

            foreach (var batch in batches)
            {
                batchNumber++;
                var batchStartTime = DateTime.UtcNow;
                
                try
                {
                    var batchArray = batch.ToArray();
                    var indexDocuments = ConvertToIndexDocuments(batchArray, indexName);
                    
                    _logger.LogDebug("Processing batch {BatchNumber} with {BatchSize} documents for {IndexName}",
                        batchNumber, batchArray.Length, indexName);
                    
                    // Use the search client to index the batch
                    var batchSuccess = await _searchClient.IndexDocumentsAsync(indexDocuments, cancellationToken);
                    
                    if (batchSuccess)
                    {
                        totalIndexed += batchArray.Length;
                        var batchTime = DateTime.UtcNow - batchStartTime;
                        _logger.LogDebug("Successfully indexed batch {BatchNumber} of {BatchSize} documents in {BatchTime:F2}s", 
                            batchNumber, batchArray.Length, batchTime.TotalSeconds);
                    }
                    else
                    {
                        var errorMsg = $"Failed to index batch {batchNumber} of {batchArray.Length} documents";
                        errors.Add(errorMsg);
                        _logger.LogWarning(errorMsg);
                    }
                }
                catch (Exception ex)
                {
                    var errorMsg = $"Error indexing batch {batchNumber}: {ex.Message}";
                    errors.Add(errorMsg);
                    _logger.LogError(ex, "Error indexing batch {BatchNumber} to {IndexName}", batchNumber, indexName);
                }
            }

            result.Success = totalIndexed > 0;
            result.DocumentsIndexed = totalIndexed;
            result.Errors = errors;
            result.Message = result.Success
                ? $"Successfully indexed {totalIndexed}/{filteredDocuments.Count} documents"
                : $"Failed to index documents to {indexName}";

            // Log indexing metrics
            LogIndexingMetrics(indexName, totalIndexed, DateTime.UtcNow - startTime, errors);

            _logger.LogInformation("Batch indexing completed for {IndexName}. Indexed: {IndexedCount}/{TotalCount}",
                indexName, totalIndexed, filteredDocuments.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error during batch indexing to {IndexName}", indexName);
            result.Success = false;
            result.Message = $"Fatal indexing error: {ex.Message}";
            result.Errors.Add(ex.Message);
        }
        finally
        {
            result.IndexingTime = DateTime.UtcNow - startTime;
        }

        return result;
    }

    private object[] ConvertToIndexDocuments(MotorcycleDocument[] documents, string indexName)
    {
        var indexDocuments = new List<object>();

        foreach (var doc in documents)
        {
            try
            {
                var indexDoc = CreateIndexDocument(doc, indexName);
                indexDocuments.Add(indexDoc);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to convert document {DocumentId} for indexing", doc.Id);
            }
        }

        return indexDocuments.ToArray();
    }

    private object CreateIndexDocument(MotorcycleDocument doc, string indexName)
    {
        // Apply field mapping and metadata management
        var fieldMapping = GetFieldMappingForIndex(indexName);
        var metadataConfig = GetMetadataConfigurationForIndex(indexName);
        
        // Extract motorcycle information from metadata with field mapping
        var make = ApplyFieldMapping(doc.Metadata, "Make", "", fieldMapping);
        var model = ApplyFieldMapping(doc.Metadata, "Model", "", fieldMapping);
        var year = ApplyFieldMapping(doc.Metadata, "Year", "", fieldMapping);

        // Base document structure with required fields
        var indexDoc = new Dictionary<string, object>
        {
            ["id"] = doc.Id,
            ["title"] = doc.Title,
            ["content"] = doc.Content,
            ["contentVector"] = doc.ContentVector ?? Array.Empty<float>(),
            ["documentType"] = doc.Type.ToString(),
            ["make"] = make,
            ["model"] = model,
            ["year"] = year,
            ["sourceFile"] = doc.Metadata.SourceFile,
            ["section"] = doc.Metadata.Section,
            ["createdAt"] = doc.CreatedAt,
            ["updatedAt"] = doc.UpdatedAt,
            ["tags"] = doc.Metadata.Tags.ToArray(),
            ["metadata"] = JsonSerializer.Serialize(doc.Metadata.AdditionalProperties)
        };

        // Add index-specific fields with metadata management
        if (indexName == PDF_INDEX_NAME || indexName == UNIFIED_INDEX_NAME)
        {
            indexDoc["pageNumber"] = doc.Metadata.PageNumber;
            indexDoc["sourceUrl"] = doc.Metadata.SourceUrl;
            indexDoc["author"] = doc.Metadata.Author;
            indexDoc["publishedDate"] = doc.Metadata.PublishedDate;
            indexDoc["language"] = ApplyFieldMapping(doc.Metadata, "Language", "en", fieldMapping);
            
            if (indexName == PDF_INDEX_NAME)
            {
                indexDoc["chunkType"] = ApplyFieldMapping(doc.Metadata, "ChunkType", "Text", fieldMapping);
            }
        }

        if (indexName == UNIFIED_INDEX_NAME)
        {
            indexDoc["sourceType"] = doc.Type == DocumentType.Specification ? "CSV" : "PDF";
        }

        // Apply metadata configuration
        ApplyMetadataConfiguration(indexDoc, doc, metadataConfig);

        return indexDoc;
    }

    private FieldMappingConfiguration GetFieldMappingForIndex(string indexName)
    {
        // Return appropriate field mapping configuration based on index
        return new FieldMappingConfiguration
        {
            FieldMappings = new Dictionary<string, string>
            {
                ["Make"] = "make",
                ["Model"] = "model", 
                ["Year"] = "year",
                ["Language"] = "language",
                ["ChunkType"] = "chunkType"
            },
            SearchableFields = new List<string> { "title", "content", "make", "model", "tags" },
            FilterableFields = new List<string> { "documentType", "make", "model", "year", "sourceFile", "section" },
            FacetableFields = new List<string> { "make", "model", "year", "documentType", "section", "tags" },
            SortableFields = new List<string> { "title", "year", "createdAt", "updatedAt", "pageNumber" }
        };
    }

    private MetadataConfiguration GetMetadataConfigurationForIndex(string indexName)
    {
        return new MetadataConfiguration
        {
            IncludeSourceMetadata = true,
            IncludeProcessingMetadata = true,
            RequiredMetadataFields = new List<string> { "make", "model", "documentType" },
            DefaultMetadataValues = new Dictionary<string, object>
            {
                ["language"] = "en",
                ["chunkType"] = "Text",
                ["sourceType"] = indexName == UNIFIED_INDEX_NAME ? "Mixed" : 
                               indexName == CSV_INDEX_NAME ? "CSV" : "PDF"
            }
        };
    }

    private string ApplyFieldMapping(DocumentMetadata metadata, string sourceField, string defaultValue, FieldMappingConfiguration fieldMapping)
    {
        // Apply field mapping if configured
        var mappedField = fieldMapping.FieldMappings.ContainsKey(sourceField) 
            ? fieldMapping.FieldMappings[sourceField] 
            : sourceField;
            
        return ExtractMetadataValue(metadata, sourceField, defaultValue);
    }

    private void ApplyMetadataConfiguration(Dictionary<string, object> indexDoc, MotorcycleDocument doc, MetadataConfiguration metadataConfig)
    {
        // Apply default metadata values
        foreach (var defaultValue in metadataConfig.DefaultMetadataValues)
        {
            if (!indexDoc.ContainsKey(defaultValue.Key))
            {
                indexDoc[defaultValue.Key] = defaultValue.Value;
            }
        }

        // Validate required metadata fields
        foreach (var requiredField in metadataConfig.RequiredMetadataFields)
        {
            if (!indexDoc.ContainsKey(requiredField) || 
                indexDoc[requiredField] == null || 
                string.IsNullOrEmpty(indexDoc[requiredField].ToString()))
            {
                _logger.LogWarning("Required metadata field '{Field}' is missing or empty for document {DocumentId}", 
                    requiredField, doc.Id);
                
                // Set a default value to prevent indexing failure
                indexDoc[requiredField] = "Unknown";
            }
        }

        // Add processing metadata if enabled
        if (metadataConfig.IncludeProcessingMetadata)
        {
            indexDoc["indexedAt"] = DateTime.UtcNow;
            indexDoc["processingVersion"] = "1.0";
        }
    }

    private string ExtractMetadataValue(DocumentMetadata metadata, string key, string defaultValue)
    {
        if (metadata.AdditionalProperties.TryGetValue(key, out var value))
        {
            return value?.ToString() ?? defaultValue;
        }
        return defaultValue;
    }

    /// <summary>
    /// Validates batch size and adjusts if necessary to meet requirements (100-1000 documents per batch)
    /// </summary>
    private int ValidateBatchSize(int requestedSize, int documentCount)
    {
        const int MIN_BATCH_SIZE = 100;
        const int MAX_BATCH_SIZE = 1000;
        
        // Ensure batch size is within acceptable range
        var adjustedSize = Math.Max(MIN_BATCH_SIZE, Math.Min(MAX_BATCH_SIZE, requestedSize));
        
        // If we have fewer documents than minimum batch size, use document count
        if (documentCount < MIN_BATCH_SIZE)
        {
            adjustedSize = documentCount;
        }
        
        if (adjustedSize != requestedSize)
        {
            _logger.LogDebug("Adjusted batch size from {RequestedSize} to {AdjustedSize} (documents: {DocumentCount})",
                requestedSize, adjustedSize, documentCount);
        }
        
        return adjustedSize;
    }

    /// <summary>
    /// Monitors indexing performance and logs metrics
    /// </summary>
    private void LogIndexingMetrics(string indexName, int documentsProcessed, TimeSpan processingTime, List<string> errors)
    {
        var documentsPerSecond = documentsProcessed / Math.Max(processingTime.TotalSeconds, 0.001);
        var errorRate = errors.Count / (double)Math.Max(documentsProcessed, 1) * 100;
        
        _logger.LogInformation("Indexing metrics for {IndexName}: {DocumentsProcessed} docs, " +
                              "{ProcessingTime:F2}s, {DocsPerSecond:F2} docs/sec, {ErrorRate:F2}% error rate",
            indexName, documentsProcessed, processingTime.TotalSeconds, documentsPerSecond, errorRate);
            
        if (errors.Count > 0)
        {
            _logger.LogWarning("Indexing errors for {IndexName}: {ErrorCount} errors", indexName, errors.Count);
            foreach (var error in errors.Take(5)) // Log first 5 errors to avoid log spam
            {
                _logger.LogWarning("Indexing error: {Error}", error);
            }
            
            if (errors.Count > 5)
            {
                _logger.LogWarning("... and {AdditionalErrors} more errors", errors.Count - 5);
            }
        }
    }

    #endregion

    public void Dispose()
    {
        _indexingSemaphore?.Dispose();
    }
}
