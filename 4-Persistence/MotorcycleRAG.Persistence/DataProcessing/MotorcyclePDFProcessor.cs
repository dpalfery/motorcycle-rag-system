using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Core.Interfaces;
using MotorcycleRAG.Core.Models;
using System.Text;
using System.Text.RegularExpressions;

namespace MotorcycleRAG.Infrastructure.DataProcessing;

/// <summary>
/// PDF processor for motorcycle manuals and documentation with semantic chunking and multimodal support
/// </summary>
public class MotorcyclePDFProcessor : IDataProcessor<PDFDocument>
{
    private readonly IDocumentIntelligenceClient _documentClient;
    private readonly IAzureOpenAIClient _openAIClient;
    private readonly IAzureSearchClient _searchClient;
    private readonly PDFProcessingConfiguration _config;
    private readonly AzureAIConfiguration _azureConfig;
    private readonly ILogger<MotorcyclePDFProcessor> _logger;

    public MotorcyclePDFProcessor(
        IDocumentIntelligenceClient documentClient,
        IAzureOpenAIClient openAIClient,
        IAzureSearchClient searchClient,
        IOptions<PDFProcessingConfiguration> config,
        IOptions<AzureAIConfiguration> azureConfig,
        ILogger<MotorcyclePDFProcessor> logger)
    {
        _documentClient = documentClient ?? throw new ArgumentNullException(nameof(documentClient));
        _openAIClient = openAIClient ?? throw new ArgumentNullException(nameof(openAIClient));
        _searchClient = searchClient ?? throw new ArgumentNullException(nameof(searchClient));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _azureConfig = azureConfig?.Value ?? throw new ArgumentNullException(nameof(azureConfig));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ProcessingResult> ProcessAsync(PDFDocument input)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = new ProcessingResult();

        try
        {
            _logger.LogInformation("Starting PDF processing for document: {FileName}", input.FileName);

            // Step 1: Extract text and structure using Document Intelligence
            var analysisResult = await ExtractDocumentContentAsync(input);
            
            // Step 2: Process multimodal content if images are present
            var multimodalContent = new List<string>();
            if (input.ContainsImages && _config.ProcessImages)
            {
                multimodalContent = await ProcessMultimodalContentAsync(input, analysisResult);
            }

            // Step 3: Implement semantic chunking with embedding-based boundaries
            var chunks = await CreateSemanticChunksAsync(analysisResult, multimodalContent, input);

            // Step 4: Generate embeddings for all chunks
            await GenerateEmbeddingsAsync(chunks);

            // Step 5: Create MotorcycleDocument objects
            var documents = await CreateMotorcycleDocumentsAsync(chunks, input, analysisResult);

            result.Success = true;
            result.Data = new ProcessedData
            {
                Id = Guid.NewGuid().ToString(),
                Documents = documents,
                Metadata = CreateProcessingMetadata(input, analysisResult, chunks.Count),
                ProcessedAt = DateTime.UtcNow
            };
            result.ItemsProcessed = documents.Count;
            result.Message = $"Successfully processed PDF with {documents.Count} document chunks";

            _logger.LogInformation("PDF processing completed successfully for {FileName}. Created {ChunkCount} chunks in {ElapsedMs}ms",
                input.FileName, documents.Count, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing PDF document: {FileName}", input.FileName);
            result.Success = false;
            result.Message = $"Failed to process PDF: {ex.Message}";
            result.Errors.Add(ex.Message);
        }
        finally
        {
            stopwatch.Stop();
            result.ProcessingTime = stopwatch.Elapsed;
        }

        return result;
    }

    public async Task<IndexingResult> IndexAsync(ProcessedData data)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = new IndexingResult();

        try
        {
            _logger.LogInformation("Starting indexing for {DocumentCount} PDF documents", data.Documents.Count);

            // Index documents in batches for efficiency
            const int batchSize = 100;
            var batches = data.Documents.Chunk(batchSize);
            var totalIndexed = 0;

            foreach (var batch in batches)
            {
                var batchArray = batch.ToArray();
                var indexSuccess = await _searchClient.IndexDocumentsAsync(batchArray);
                if (indexSuccess)
                {
                    totalIndexed += batchArray.Length;
                }
                else
                {
                    result.Errors.Add($"Failed to index batch of {batchArray.Length} documents");
                }
            }

            result.Success = result.Errors.Count == 0;
            result.DocumentsIndexed = totalIndexed;
            result.IndexName = "motorcycle-pdf-index";
            result.Message = result.Success 
                ? $"Successfully indexed {totalIndexed} PDF documents"
                : $"Indexed {totalIndexed} documents with {result.Errors.Count} errors";

            _logger.LogInformation("PDF indexing completed. Indexed {IndexedCount}/{TotalCount} documents in {ElapsedMs}ms",
                totalIndexed, data.Documents.Count, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error indexing PDF documents");
            result.Success = false;
            result.Message = $"Failed to index PDF documents: {ex.Message}";
            result.Errors.Add(ex.Message);
        }
        finally
        {
            stopwatch.Stop();
            result.IndexingTime = stopwatch.Elapsed;
        }

        return result;
    }

    private async Task<DocumentAnalysisResult> ExtractDocumentContentAsync(PDFDocument input)
    {
        _logger.LogDebug("Extracting content from PDF using Document Intelligence");

        // Convert stream to byte array
        using var memoryStream = new MemoryStream();
        await input.Content.CopyToAsync(memoryStream);
        var documentBytes = memoryStream.ToArray();

        // Use Document Intelligence Layout model for text extraction
        var analysisResult = await _documentClient.AnalyzeDocumentAsync(
            documentBytes, 
            "application/pdf");

        _logger.LogDebug("Document Intelligence extraction completed. Pages: {PageCount}, Tables: {TableCount}",
            analysisResult.Pages.Length, analysisResult.Tables.Length);

        return analysisResult;
    }

    private async Task<List<string>> ProcessMultimodalContentAsync(PDFDocument input, DocumentAnalysisResult analysisResult)
    {
        _logger.LogDebug("Processing multimodal content using GPT-4 Vision");

        var multimodalContent = new List<string>();

        try
        {
            // In a real implementation, you would extract images from the PDF
            // For now, we'll simulate processing the document as a whole image
            using var memoryStream = new MemoryStream();
            await input.Content.CopyToAsync(memoryStream);
            var documentBytes = memoryStream.ToArray();

            var prompt = $@"Analyze this motorcycle manual page and describe:
1. Any diagrams, schematics, or technical illustrations
2. Parts identification and labeling
3. Visual instructions or procedures
4. Safety warnings or cautions shown visually
5. Any technical specifications displayed in visual format

Focus on motorcycle-specific technical content that would be valuable for mechanics and enthusiasts.";

            var visionAnalysis = await _openAIClient.ProcessMultimodalContentAsync(
                _azureConfig.Models.VisionModel ?? "gpt-4-vision",
                prompt,
                documentBytes,
                "application/pdf");

            multimodalContent.Add(visionAnalysis);

            _logger.LogDebug("GPT-4 Vision processing completed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process multimodal content, continuing with text-only processing");
        }

        return multimodalContent;
    }

    private async Task<List<PDFChunk>> CreateSemanticChunksAsync(
        DocumentAnalysisResult analysisResult, 
        List<string> multimodalContent, 
        PDFDocument input)
    {
        _logger.LogDebug("Creating semantic chunks with embedding-based boundary detection");

        var chunks = new List<PDFChunk>();
        var chunkId = 0;

        // Process each page
        foreach (var page in analysisResult.Pages)
        {
            var pageChunks = await CreatePageChunksAsync(page, input, chunkId);
            chunks.AddRange(pageChunks);
            chunkId += pageChunks.Count;
        }

        // Process tables separately
        foreach (var table in analysisResult.Tables)
        {
            var tableChunk = CreateTableChunk(table, input, chunkId++);
            chunks.Add(tableChunk);
        }

        // Add multimodal content as separate chunks
        foreach (var content in multimodalContent)
        {
            var multimodalChunk = new PDFChunk
            {
                Id = $"{input.FileName}_multimodal_{chunkId++}",
                Content = content,
                PageNumber = 0, // Multimodal content spans multiple pages
                Section = "Visual Analysis",
                Type = ChunkType.Image,
                Metadata = new Dictionary<string, object>
                {
                    ["Source"] = "GPT-4 Vision",
                    ["ContentType"] = "Multimodal Analysis"
                }
            };
            chunks.Add(multimodalChunk);
        }

        // Apply semantic boundary detection
        if (_config.PreserveStructure)
        {
            chunks = await RefineChunkBoundariesAsync(chunks);
        }

        _logger.LogDebug("Created {ChunkCount} semantic chunks", chunks.Count);
        return chunks;
    }

    private async Task<List<PDFChunk>> CreatePageChunksAsync(DocumentPage page, PDFDocument input, int startingChunkId)
    {
        var chunks = new List<PDFChunk>();
        var content = page.Content;

        if (string.IsNullOrWhiteSpace(content))
            return chunks;

        // Detect sections using headers and structure
        var sections = DetectSections(content);
        var chunkId = startingChunkId;

        foreach (var section in sections)
        {
            // Split section into chunks based on size limits
            var sectionChunks = SplitTextIntoChunks(section.Content, _config.MaxChunkSize, _config.MinChunkSize, _config.ChunkOverlap);
            
            foreach (var chunkContent in sectionChunks)
            {
                var chunk = new PDFChunk
                {
                    Id = $"{input.FileName}_page_{page.PageNumber}_chunk_{chunkId++}",
                    Content = chunkContent,
                    PageNumber = page.PageNumber,
                    Section = section.Title,
                    Type = ChunkType.Text,
                    Metadata = new Dictionary<string, object>
                    {
                        ["PageWidth"] = page.Width,
                        ["PageHeight"] = page.Height,
                        ["SectionType"] = section.Type
                    }
                };
                chunks.Add(chunk);
            }
        }

        return chunks;
    }

    private PDFChunk CreateTableChunk(DocumentTable table, PDFDocument input, int chunkId)
    {
        var tableContent = new StringBuilder();
        tableContent.AppendLine($"Table with {table.RowCount} rows and {table.ColumnCount} columns:");

        // Convert table to readable text format
        var rows = table.Cells.GroupBy(c => c.RowIndex).OrderBy(g => g.Key);
        foreach (var row in rows)
        {
            var cells = row.OrderBy(c => c.ColumnIndex).Select(c => c.Content);
            tableContent.AppendLine(string.Join(" | ", cells));
        }

        return new PDFChunk
        {
            Id = $"{input.FileName}_table_{chunkId}",
            Content = tableContent.ToString(),
            PageNumber = 0, // Tables might span multiple pages
            Section = "Table Data",
            Type = ChunkType.Table,
            Metadata = new Dictionary<string, object>
            {
                ["RowCount"] = table.RowCount,
                ["ColumnCount"] = table.ColumnCount,
                ["CellCount"] = table.Cells.Length
            }
        };
    }

    private List<DocumentSection> DetectSections(string content)
    {
        var sections = new List<DocumentSection>();
        
        // Simple section detection based on common patterns in motorcycle manuals
        var headerPatterns = new[]
        {
            @"^\d+\.\s+(.+)$", // Numbered sections (1. Introduction)
            @"^([A-Z][A-Z\s]+)$", // ALL CAPS headers
            @"^([A-Z][a-z\s]+):$", // Title case with colon
            @"^(CHAPTER\s+\d+.*)$", // Chapter headers
            @"^(SECTION\s+\d+.*)$" // Section headers
        };

        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var currentSection = new DocumentSection { Title = "Introduction", Content = new StringBuilder(), Type = "General" };
        
        foreach (var line in lines)
        {
            var isHeader = false;
            foreach (var pattern in headerPatterns)
            {
                var match = Regex.Match(line.Trim(), pattern, RegexOptions.Multiline);
                if (match.Success)
                {
                    // Save current section
                    if (currentSection.Content.Length > 0)
                    {
                        sections.Add(currentSection);
                    }
                    
                    // Start new section
                    currentSection = new DocumentSection
                    {
                        Title = match.Groups[1].Value.Trim(),
                        Content = new StringBuilder(),
                        Type = DetermineContentType(match.Groups[1].Value)
                    };
                    isHeader = true;
                    break;
                }
            }

            if (!isHeader)
            {
                currentSection.Content.AppendLine(line);
            }
        }

        // Add the last section
        if (currentSection.Content.Length > 0)
        {
            sections.Add(currentSection);
        }

        return sections;
    }

    private string DetermineContentType(string sectionTitle)
    {
        var title = sectionTitle.ToLowerInvariant();
        
        if (title.Contains("maintenance") || title.Contains("service"))
            return "Maintenance";
        if (title.Contains("specification") || title.Contains("spec"))
            return "Specification";
        if (title.Contains("troubleshoot") || title.Contains("problem"))
            return "Troubleshooting";
        if (title.Contains("safety") || title.Contains("warning"))
            return "Safety";
        if (title.Contains("installation") || title.Contains("assembly"))
            return "Installation";
        
        return "General";
    }

    private List<string> SplitTextIntoChunks(StringBuilder content, int maxChunkSize, int minChunkSize, int overlap)
    {
        var text = content.ToString();
        var chunks = new List<string>();
        
        if (text.Length <= maxChunkSize)
        {
            chunks.Add(text);
            return chunks;
        }

        var sentences = text.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
        var currentChunk = new StringBuilder();
        
        foreach (var sentence in sentences)
        {
            var trimmedSentence = sentence.Trim();
            if (string.IsNullOrEmpty(trimmedSentence)) continue;

            // Check if adding this sentence would exceed the max chunk size
            if (currentChunk.Length + trimmedSentence.Length + 1 > maxChunkSize && currentChunk.Length >= minChunkSize)
            {
                chunks.Add(currentChunk.ToString().Trim());
                
                // Start new chunk with overlap
                var overlapText = GetOverlapText(currentChunk.ToString(), overlap);
                currentChunk = new StringBuilder(overlapText);
            }

            currentChunk.Append(trimmedSentence).Append(". ");
        }

        // Add the last chunk
        if (currentChunk.Length >= minChunkSize)
        {
            chunks.Add(currentChunk.ToString().Trim());
        }

        return chunks;
    }

    private string GetOverlapText(string text, int overlapSize)
    {
        if (text.Length <= overlapSize) return text;
        
        var startIndex = text.Length - overlapSize;
        var overlapText = text.Substring(startIndex);
        
        // Try to start at a sentence boundary
        var sentenceStart = overlapText.IndexOf(". ");
        if (sentenceStart > 0 && sentenceStart < overlapSize / 2)
        {
            overlapText = overlapText.Substring(sentenceStart + 2);
        }
        
        return overlapText;
    }

    private async Task<List<PDFChunk>> RefineChunkBoundariesAsync(List<PDFChunk> chunks)
    {
        _logger.LogDebug("Refining chunk boundaries using embedding-based similarity");

        // For chunks that are too similar, merge them
        // For chunks that are too different, consider splitting them further
        var refinedChunks = new List<PDFChunk>();
        
        for (int i = 0; i < chunks.Count; i++)
        {
            var currentChunk = chunks[i];
            
            // Check similarity with next chunk if it exists
            if (i < chunks.Count - 1)
            {
                var nextChunk = chunks[i + 1];
                
                // Generate embeddings for similarity comparison
                var embeddings = await _openAIClient.GetEmbeddingsAsync(
                    _azureConfig.Models.EmbeddingModel,
                    new[] { currentChunk.Content, nextChunk.Content });

                var similarity = CalculateCosineSimilarity(embeddings[0], embeddings[1]);
                
                // If chunks are very similar and from the same section, consider merging
                if (similarity > _config.SimilarityThreshold && 
                    currentChunk.Section == nextChunk.Section &&
                    currentChunk.Content.Length + nextChunk.Content.Length <= _config.MaxChunkSize)
                {
                    // Merge chunks
                    var mergedChunk = new PDFChunk
                    {
                        Id = $"{currentChunk.Id}_merged",
                        Content = $"{currentChunk.Content}\n\n{nextChunk.Content}",
                        PageNumber = currentChunk.PageNumber,
                        Section = currentChunk.Section,
                        Type = currentChunk.Type,
                        Metadata = currentChunk.Metadata
                    };
                    
                    refinedChunks.Add(mergedChunk);
                    i++; // Skip the next chunk as it's been merged
                    continue;
                }
            }
            
            refinedChunks.Add(currentChunk);
        }

        _logger.LogDebug("Refined {OriginalCount} chunks to {RefinedCount} chunks", chunks.Count, refinedChunks.Count);
        return refinedChunks;
    }

    private float CalculateCosineSimilarity(float[] vectorA, float[] vectorB)
    {
        if (vectorA.Length != vectorB.Length)
            throw new ArgumentException("Vectors must have the same length");

        var dotProduct = vectorA.Zip(vectorB, (a, b) => a * b).Sum();
        var magnitudeA = Math.Sqrt(vectorA.Sum(a => a * a));
        var magnitudeB = Math.Sqrt(vectorB.Sum(b => b * b));

        if (magnitudeA == 0 || magnitudeB == 0)
            return 0;

        return (float)(dotProduct / (magnitudeA * magnitudeB));
    }

    private async Task GenerateEmbeddingsAsync(List<PDFChunk> chunks)
    {
        _logger.LogDebug("Generating embeddings for {ChunkCount} chunks", chunks.Count);

        // Process chunks in batches for efficiency
        const int batchSize = 10;
        var batches = chunks.Chunk(batchSize);

        foreach (var batch in batches)
        {
            var batchArray = batch.ToArray();
            var texts = batchArray.Select(c => c.Content).ToArray();
            var embeddings = await _openAIClient.GetEmbeddingsAsync(_azureConfig.Models.EmbeddingModel, texts);

            for (int i = 0; i < batchArray.Length && i < embeddings.Length; i++)
            {
                batchArray[i].Embedding = embeddings[i];
            }
        }

        _logger.LogDebug("Embedding generation completed");
    }

    private async Task<List<MotorcycleDocument>> CreateMotorcycleDocumentsAsync(
        List<PDFChunk> chunks, 
        PDFDocument input, 
        DocumentAnalysisResult analysisResult)
    {
        _logger.LogDebug("Creating MotorcycleDocument objects from chunks");

        var documents = new List<MotorcycleDocument>();

        foreach (var chunk in chunks)
        {
            var document = new MotorcycleDocument
            {
                Id = chunk.Id,
                Title = $"{input.Make} {input.Model} {input.Year} - {chunk.Section}",
                Content = chunk.Content,
                Type = DocumentType.Manual,
                ContentVector = chunk.Embedding,
                Metadata = new DocumentMetadata
                {
                    SourceFile = input.FileName,
                    SourceUrl = input.Source,
                    PageNumber = chunk.PageNumber,
                    Section = chunk.Section,
                    Author = $"{input.Make} {input.Model}",
                    PublishedDate = input.UploadedAt,
                    Tags = new List<string> { input.Make, input.Model, input.Year, input.DocumentType.ToString() },
                    AdditionalProperties = new Dictionary<string, object>
                    {
                        ["SourceType"] = "PDF",
                        ["Make"] = input.Make,
                        ["Model"] = input.Model,
                        ["Year"] = input.Year,
                        ["DocumentType"] = input.DocumentType.ToString(),
                        ["ChunkType"] = chunk.Type.ToString(),
                        ["ProcessedAt"] = DateTime.UtcNow,
                        ["Language"] = input.Language,
                        ["ChunkMetadata"] = chunk.Metadata
                    }
                }
            };

            documents.Add(document);
        }

        _logger.LogDebug("Created {DocumentCount} MotorcycleDocument objects", documents.Count);
        return documents;
    }

    private Dictionary<string, object> CreateProcessingMetadata(
        PDFDocument input, 
        DocumentAnalysisResult analysisResult, 
        int chunkCount)
    {
        return new Dictionary<string, object>
        {
            ["OriginalFileName"] = input.FileName,
            ["FileSizeBytes"] = input.FileSizeBytes,
            ["DocumentType"] = input.DocumentType.ToString(),
            ["Make"] = input.Make,
            ["Model"] = input.Model,
            ["Year"] = input.Year,
            ["Language"] = input.Language,
            ["PageCount"] = analysisResult.Pages.Length,
            ["TableCount"] = analysisResult.Tables.Length,
            ["ChunkCount"] = chunkCount,
            ["ProcessingConfiguration"] = new
            {
                MaxChunkSize = _config.MaxChunkSize,
                MinChunkSize = _config.MinChunkSize,
                ChunkOverlap = _config.ChunkOverlap,
                SimilarityThreshold = _config.SimilarityThreshold,
                ProcessImages = _config.ProcessImages,
                PreserveStructure = _config.PreserveStructure
            },
            ["ProcessedAt"] = DateTime.UtcNow
        };
    }

    private class DocumentSection
    {
        public string Title { get; set; } = string.Empty;
        public StringBuilder Content { get; set; } = new();
        public string Type { get; set; } = string.Empty;
    }
}