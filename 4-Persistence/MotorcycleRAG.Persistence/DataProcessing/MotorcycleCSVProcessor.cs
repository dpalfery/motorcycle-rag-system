using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Core.Interfaces;
using MotorcycleRAG.Core.Models;

namespace MotorcycleRAG.Infrastructure.DataProcessing;

/// <summary>
/// Processes CSV files containing motorcycle specifications with row-based chunking
/// and embedding generation using text-embedding-3-large model
/// </summary>
public class MotorcycleCSVProcessor : IDataProcessor<CSVFile>
{
    private readonly IAzureOpenAIClient _openAIClient;
    private readonly IAzureSearchClient _searchClient;
    private readonly ILogger<MotorcycleCSVProcessor> _logger;
    private readonly CSVProcessingConfiguration _configuration;

    public MotorcycleCSVProcessor(
        IAzureOpenAIClient openAIClient,
        IAzureSearchClient searchClient,
        ILogger<MotorcycleCSVProcessor> logger,
        CSVProcessingConfiguration? configuration = null)
    {
        _openAIClient = openAIClient ?? throw new ArgumentNullException(nameof(openAIClient));
        _searchClient = searchClient ?? throw new ArgumentNullException(nameof(searchClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? new CSVProcessingConfiguration();
    }

    /// <summary>
    /// Process CSV file with row-based chunking preserving relational integrity
    /// </summary>
    public async Task<ProcessingResult> ProcessAsync(CSVFile input)
    {
        var startTime = DateTime.UtcNow;
        var result = new ProcessingResult();
        var documents = new List<MotorcycleDocument>();
        var errors = new List<string>();

        try
        {
            _logger.LogInformation("Starting CSV processing for file: {FileName}", input.FileName);

            // Validate input
            if (!ValidateInput(input, errors))
            {
                result.Success = false;
                result.Errors = errors;
                result.Message = "Input validation failed";
                return result;
            }

            // Parse CSV and create chunks
            var chunks = await ParseCSVIntoChunksAsync(input);
            _logger.LogInformation("Created {ChunkCount} chunks from CSV file", chunks.Count);

            // Process each chunk
            var processedCount = 0;
            foreach (var chunk in chunks)
            {
                try
                {
                    var document = await ProcessChunkAsync(chunk, input.FileName, processedCount);
                    documents.Add(document);
                    processedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing chunk {ChunkIndex}", processedCount);
                    errors.Add($"Error processing chunk {processedCount}: {ex.Message}");
                }
            }

            result.Success = documents.Count > 0;
            result.Data = new ProcessedData
            {
                Id = Guid.NewGuid().ToString(),
                Documents = documents,
                Metadata = new Dictionary<string, object>
                {
                    ["SourceFile"] = input.FileName,
                    ["ChunksCreated"] = chunks.Count,
                    ["ProcessingConfiguration"] = _configuration
                }
            };
            result.ItemsProcessed = processedCount;
            result.Errors = errors;
            result.Message = $"Successfully processed {documents.Count} chunks from CSV file";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error processing CSV file: {FileName}", input.FileName);
            result.Success = false;
            result.Message = $"Fatal error: {ex.Message}";
            result.Errors.Add(ex.Message);
        }
        finally
        {
            result.ProcessingTime = DateTime.UtcNow - startTime;
        }

        return result;
    }

    /// <summary>
    /// Index processed data into Azure AI Search using the dedicated indexing service
    /// </summary>
    public async Task<IndexingResult> IndexAsync(ProcessedData data)
    {
        var startTime = DateTime.UtcNow;
        var result = new IndexingResult();

        try
        {
            _logger.LogInformation("Starting CSV indexing of {DocumentCount} documents", data.Documents.Count);

            // Use the search client for basic indexing (backward compatibility)
            // In production, this would be replaced with IMotorcycleIndexingService
            var batchSize = 100; // Optimal batch size for Azure AI Search
            var batches = data.Documents.Chunk(batchSize);
            var totalIndexed = 0;
            var errors = new List<string>();

            foreach (var batch in batches)
            {
                try
                {
                    var success = await _searchClient.IndexDocumentsAsync(batch.ToArray());
                    if (success)
                    {
                        totalIndexed += batch.Count();
                    }
                    else
                    {
                        errors.Add($"Failed to index batch of {batch.Count()} documents");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error indexing batch");
                    errors.Add($"Batch indexing error: {ex.Message}");
                }
            }

            result.Success = totalIndexed > 0;
            result.DocumentsIndexed = totalIndexed;
            result.Errors = errors;
            result.Message = $"Indexed {totalIndexed} CSV documents successfully";
            result.IndexName = "motorcycle-csv-index";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error during CSV indexing");
            result.Success = false;
            result.Message = $"CSV indexing failed: {ex.Message}";
            result.Errors.Add(ex.Message);
        }
        finally
        {
            result.IndexingTime = DateTime.UtcNow - startTime;
        }

        return result;
    }

    /// <summary>
    /// Parse CSV file into chunks preserving relational integrity
    /// </summary>
    private async Task<List<CSVChunk>> ParseCSVIntoChunksAsync(CSVFile csvFile)
    {
        var chunks = new List<CSVChunk>();
        var currentChunk = new List<Dictionary<string, object>>();
        var headers = new List<string>();

        using var reader = new StreamReader(csvFile.Content, Encoding.GetEncoding(csvFile.Encoding));
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = csvFile.HasHeaders,
            Delimiter = csvFile.Delimiter,
            BadDataFound = null, // Ignore bad data
            MissingFieldFound = null // Ignore missing fields
        });

        // Read headers if present
        if (csvFile.HasHeaders)
        {
            await csv.ReadAsync();
            csv.ReadHeader();
            headers = csv.HeaderRecord?.ToList() ?? new List<string>();
            
            // Validate column count
            if (headers.Count > csvFile.MaxColumns)
            {
                throw new InvalidOperationException($"CSV has {headers.Count} columns, maximum allowed is {csvFile.MaxColumns}");
            }
        }
        else
        {
            // Generate column names for headerless CSV
            var firstRow = await csv.ReadAsync();
            if (firstRow)
            {
                var record = csv.GetRecord<dynamic>();
                var fieldCount = ((IDictionary<string, object>)record).Count;
                headers = Enumerable.Range(1, fieldCount).Select(i => $"Column{i}").ToList();
            }
        }

        var rowCount = 0;
        var chunkIndex = 0;

        // Process rows
        while (await csv.ReadAsync() && rowCount < _configuration.MaxRows)
        {
            try
            {
                var record = new Dictionary<string, object>();
                
                // Read all fields for this row
                for (int i = 0; i < headers.Count; i++)
                {
                    var fieldValue = csv.GetField(i) ?? string.Empty;
                    record[headers[i]] = fieldValue;
                }

                // Check if we should create a new chunk BEFORE adding the current record
                if (ShouldCreateNewChunk(currentChunk, record))
                {
                    if (currentChunk.Count > 0)
                    {
                        chunks.Add(new CSVChunk
                        {
                            Index = chunkIndex++,
                            Headers = headers,
                            Rows = new List<Dictionary<string, object>>(currentChunk)
                        });
                        currentChunk.Clear();
                    }
                }

                currentChunk.Add(record);
                rowCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error reading row {RowNumber}, skipping", rowCount + 1);
            }
        }

        // Add remaining rows as final chunk
        if (currentChunk.Count > 0)
        {
            chunks.Add(new CSVChunk
            {
                Index = chunkIndex,
                Headers = headers,
                Rows = currentChunk
            });
        }

        return chunks;
    }

    /// <summary>
    /// Determine if a new chunk should be created based on relational integrity
    /// </summary>
    private bool ShouldCreateNewChunk(List<Dictionary<string, object>> currentChunk, Dictionary<string, object> newRow)
    {
        // If chunk is empty, don't create a new chunk
        if (currentChunk.Count == 0)
        {
            return false;
        }

        // If preserving relational integrity, check if this row belongs to a different motorcycle
        if (_configuration.PreserveRelationalIntegrity)
        {
            var lastRow = currentChunk.LastOrDefault();
            if (lastRow != null && !IsSameMotorcycle(lastRow, newRow))
            {
                return true; // Different motorcycle, create new chunk
            }
        }

        // Create new chunk when size limit is reached (and not preserving integrity or same motorcycle)
        if (currentChunk.Count >= _configuration.ChunkSize)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Check if two rows represent the same motorcycle based on identifier fields
    /// </summary>
    private bool IsSameMotorcycle(Dictionary<string, object> row1, Dictionary<string, object> row2)
    {
        foreach (var field in _configuration.IdentifierFields)
        {
            if (row1.TryGetValue(field, out var value1) && row2.TryGetValue(field, out var value2))
            {
                if (!string.Equals(value1?.ToString(), value2?.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }
        return true;
    }

    /// <summary>
    /// Process a single chunk into a MotorcycleDocument with embeddings
    /// </summary>
    private async Task<MotorcycleDocument> ProcessChunkAsync(CSVChunk chunk, string sourceFile, int chunkIndex)
    {
        // Create content for embedding
        var contentBuilder = new StringBuilder();
        
        foreach (var row in chunk.Rows)
        {
            var rowContent = string.Join(" | ", row.Select(kvp => $"{kvp.Key}: {kvp.Value}"));
            contentBuilder.AppendLine(rowContent);
        }

        var content = contentBuilder.ToString();
        
        // Generate title from first row's key fields
        var title = GenerateChunkTitle(chunk.Rows.FirstOrDefault(), chunkIndex);

        // Generate embedding using text-embedding-3-large
        var embedding = await _openAIClient.GetEmbeddingAsync("text-embedding-3-large", content);

        return new MotorcycleDocument
        {
            Id = $"csv-chunk-{Guid.NewGuid()}",
            Title = title,
            Content = content,
            Type = DocumentType.Specification,
            ContentVector = embedding,
            Metadata = new DocumentMetadata
            {
                SourceFile = sourceFile,
                Section = $"Chunk {chunkIndex}",
                AdditionalProperties = new Dictionary<string, object>
                {
                    ["ChunkIndex"] = chunkIndex,
                    ["RowCount"] = chunk.Rows.Count,
                    ["Headers"] = chunk.Headers,
                    ["ProcessingMethod"] = "RowBasedChunking"
                }
            }
        };
    }

    /// <summary>
    /// Generate a descriptive title for a chunk based on motorcycle identifiers
    /// </summary>
    private string GenerateChunkTitle(Dictionary<string, object>? firstRow, int chunkIndex)
    {
        if (firstRow == null)
        {
            return $"Motorcycle Specifications - Chunk {chunkIndex}";
        }

        var titleParts = new List<string>();
        
        foreach (var field in _configuration.IdentifierFields)
        {
            if (firstRow.TryGetValue(field, out var value) && !string.IsNullOrWhiteSpace(value?.ToString()))
            {
                titleParts.Add(value.ToString()!);
            }
        }

        return titleParts.Count > 0 
            ? $"{string.Join(" ", titleParts)} - Specifications"
            : $"Motorcycle Specifications - Chunk {chunkIndex}";
    }

    /// <summary>
    /// Validate CSV input
    /// </summary>
    private bool ValidateInput(CSVFile input, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(input.FileName))
        {
            errors.Add("File name is required");
        }

        if (input.Content == null || input.Content == Stream.Null)
        {
            errors.Add("File content is required");
        }

        if (input.Content?.Length == 0)
        {
            errors.Add("File content cannot be empty");
        }

        return errors.Count == 0;
    }
}

/// <summary>
/// Represents a chunk of CSV data
/// </summary>
internal class CSVChunk
{
    public int Index { get; set; }
    public List<string> Headers { get; set; } = new();
    public List<Dictionary<string, object>> Rows { get; set; } = new();
}