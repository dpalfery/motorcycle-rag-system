using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Utilities;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;
using MotorcycleRAG.Domain.ValueObjects;
using System.Text;
using System.Text.RegularExpressions;

namespace MotorcycleRAG.Persistence.DataProcessing;

/// <summary>
/// PDF processor for motorcycle manuals and documentation with semantic chunking and multimodal support
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S1200:Split this class into smaller and more specialized ones", Justification = "Complex processor requires multiple service integrations")]
public class MotorcyclePdfProcessor : IDataProcessor<PDFDocument> {
    private readonly IDocumentIntelligenceClient _documentClient;
    private readonly IAzureFoundryClient _openAIClient;
    private readonly IAzureSearchClient _searchClient;
    private readonly PDFProcessingConfiguration _config;
    private readonly AzureFoundryOptions _azureConfig;
    private readonly ILogger<MotorcyclePdfProcessor> _logger;
    private static readonly char[] SentenceEndCharacters = { '.', '!', '?' };

    public MotorcyclePdfProcessor(
        IDocumentIntelligenceClient documentClient,
        IAzureFoundryClient openAIClient,
        IAzureSearchClient searchClient,
        IOptions<PDFProcessingConfiguration> config,
        IOptions<AzureFoundryOptions> azureConfig,
        ILogger<MotorcyclePdfProcessor> logger) {
        ArgumentNullException.ThrowIfNull(documentClient);
        ArgumentNullException.ThrowIfNull(openAIClient);
        ArgumentNullException.ThrowIfNull(searchClient);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(azureConfig);
        ArgumentNullException.ThrowIfNull(logger);

        _documentClient = documentClient;
        _openAIClient = openAIClient;
        _searchClient = searchClient;
        _config = config.Value ?? throw new ArgumentNullException(nameof(config));
        _azureConfig = azureConfig.Value ?? throw new ArgumentNullException(nameof(azureConfig));
        _logger = logger;
    }

    public async Task<ProcessedData> ProcessAsync(PDFDocument input) {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        ArgumentNullException.ThrowIfNull(input);

        try {
            _logger.LogInformation("Starting PDF processing for document: {FileName}",
                LogSanitizer.Sanitize(input.FileName));

            // Step 1: Extract text and structure using Document Intelligence
            var analysisResult = await ExtractDocumentContentAsync(input);

            // Step 2: Process multimodal content if images are present
            var multimodalContent = new List<string>();
            if (input.ContainsImages && _config.ProcessImages) {
                multimodalContent = await ProcessMultimodalContentAsync(input);
            }

            // Step 3: Implement semantic chunking with embedding-based boundaries
            var chunks = await CreateSemanticChunksAsync(analysisResult, multimodalContent, input);

            // Step 4: Generate embeddings for all chunks
            await GenerateEmbeddingsAsync(chunks);

            // Step 5: Create MotorcycleDocument objects
            var processedDocuments = await CreateMotorcycleDocumentsAsync(chunks, input);

            var processed = new ProcessedData();
            processed.Id = Guid.NewGuid().ToString();
            foreach (var d in processedDocuments)
                processed.Documents.Add(d);
            var meta = CreateProcessingMetadata(input, analysisResult, chunks.Count);
            foreach (var kv in meta)
                processed.Metadata[kv.Key] = kv.Value;

            return processed;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error processing PDF document: {FileName}",
                LogSanitizer.Sanitize(input.FileName));
            throw new InvalidOperationException($"Failed to process PDF: {ex.Message}", ex);
        }
        finally {
            stopwatch.Stop();
        }
    }

    public async Task<IndexingResult> IndexAsync(ProcessedData data) {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        ArgumentNullException.ThrowIfNull(data);

        var result = new IndexingResult();

        try {
            _logger.LogInformation("Starting indexing for {DocumentCount} PDF documents", data.Documents.Count);

            // Index documents in batches for efficiency
            const int batchSize = 100;
            var batches = data.Documents.Chunk(batchSize);
            var totalIndexed = 0;

            foreach (var batch in batches) {
                var batchArray = batch.ToArray();
                await _searchClient.IndexDocumentsAsync(batchArray);
                totalIndexed += batchArray.Length;
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
        catch (Exception ex) {
            _logger.LogError(ex, "Error indexing PDF documents");
            result.Success = false;
            result.Message = $"Failed to index PDF documents: {ex.Message}";
            result.Errors.Add(ex.Message);
        }
        finally {
            stopwatch.Stop();
            result.IndexingTime = stopwatch.Elapsed;
        }

        return result;
    }

    private async Task<DocumentAnalysisResult> ExtractDocumentContentAsync(PDFDocument input) {
        _logger.LogDebug("Extracting content from PDF using Document Intelligence");

        // Convert stream to byte array
        using var memoryStream = new MemoryStream();
        await input.Content.CopyToAsync(memoryStream);
        var documentBytes = memoryStream.ToArray();

        // Use Document Intelligence Layout model for text extraction
        using var stream = new MemoryStream(documentBytes);
        var analysisResult = await _documentClient.AnalyzeDocumentAsync(
            stream,
            "application/pdf");

        _logger.LogDebug("Document Intelligence extraction completed. Pages: {PageCount}, Tables: {TableCount}",
            analysisResult.Pages.Length, analysisResult.Tables.Length);

        // T053/T054: Enrich the analysis result with page/section/table metadata
        // This populates locator fields that the Document Intelligence wrapper doesn't provide
        EnrichDocumentAnalysisResult(analysisResult);

        return analysisResult;
    }

    /// <summary>
    /// T053/T054: Enriches DocumentAnalysisResult with page/section/table locator metadata
    /// Populates fields that IDocumentIntelligenceClient doesn't provide using regex/heuristics
    /// </summary>
    private void EnrichDocumentAnalysisResult(DocumentAnalysisResult analysisResult) {
        _logger.LogDebug("Enriching document analysis result with locator metadata");

        // Enrich page-level section metadata
        foreach (var page in analysisResult.Pages) {
            EnrichPageMetadata(page);
        }

        // Enrich table locator metadata
        foreach (var table in analysisResult.Tables) {
            EnrichTableMetadata(table, analysisResult.Pages);
        }

        _logger.LogDebug("Document enrichment completed");
    }

    /// <summary>
    /// T053/T054: Enriches a single page with section metadata (PrimarySection, SectionHeadings, SectionLevel)
    /// Uses regex pattern matching to detect document structure
    /// </summary>
    private void EnrichPageMetadata(DocumentPage page) {
        _logger.LogDebug("EnrichPageMetadata: Before enrichment - PrimarySection={PrimarySection}, SectionLevel={SectionLevel}",
            page.PrimarySection, page.SectionLevel);

        if (string.IsNullOrWhiteSpace(page.Content)) {
            page.PrimarySection = "Empty Page";
            page.SectionHeadings = Array.Empty<string>();
            page.SectionLevel = 0;
            _logger.LogDebug("EnrichPageMetadata: Empty page, set SectionLevel=0");
            return;
        }

        // Only enrich if metadata is not already set (for test data with pre-enriched metadata)
        if (page.SectionLevel > 0) {
            // Use pre-set values if SectionLevel is > 0
            _logger.LogDebug("EnrichPageMetadata: Skipped enrichment, using pre-set values - PrimarySection={PrimarySection}, SectionLevel={SectionLevel}",
                page.PrimarySection, page.SectionLevel);
        }
        else if (string.IsNullOrEmpty(page.PrimarySection)) {
            // Extract section metadata using regex/heuristics
            var (primarySection, headings, level) = ExtractSectionMetadata(page.Content);

            page.PrimarySection = primarySection;
            page.SectionHeadings = headings;
            page.SectionLevel = level;

            _logger.LogDebug("EnrichPageMetadata: Enriched - PrimarySection={PrimarySection}, SectionLevel={SectionLevel}",
                page.PrimarySection, page.SectionLevel);
        }
        else {
            _logger.LogDebug("EnrichPageMetadata: Skipped enrichment, using pre-set PrimarySection - PrimarySection={PrimarySection}, SectionLevel={SectionLevel}",
                page.PrimarySection, page.SectionLevel);
        }
    }

    /// <summary>
    /// T053/T054: Enriches table with locator metadata (StartPageNumber, EndPageNumber, Caption, Section)
    /// Uses heuristics from table cells and surrounding context
    /// </summary>
    private void EnrichTableMetadata(DocumentTable table, DocumentPage[] pages) {
        // Determine page range from cells
        if (table.Cells.Length > 0) {
            var minPage = table.Cells.Min(c => c.PageNumber);
            var maxPage = table.Cells.Max(c => c.PageNumber);

            // If cells have page numbers, use them; otherwise default to 1
            table.StartPageNumber = minPage > 0 ? minPage : 1;
            table.EndPageNumber = maxPage > 0 ? maxPage : table.StartPageNumber;
        }
        else {
            // Fallback for tables with no cells
            table.StartPageNumber = 1;
            table.EndPageNumber = 1;
        }

        // Extract caption from first header row or nearby text
        table.Caption = ExtractTableCaption(table);

        // Determine section from page context or table content
        table.Section = DetermineTableSection(table, pages);
    }

    /// <summary>
    /// T053/T054: Extracts table caption from header row cells using heuristics
    /// Looks for patterns like "Table 1.1:", "Table:", "Table of", etc.
    /// </summary>
    private string ExtractTableCaption(DocumentTable table) {
        // Check header cells for caption-like content
        var headerCells = table.Cells.Where(c => c.IsHeader).OrderBy(c => c.RowIndex).ThenBy(c => c.ColumnIndex).ToArray();

        if (headerCells.Length > 0) {
            // Look for caption patterns in first few header cells
            var captionPatterns = new[]
            {
                @"^Table\s+\d+[\.\d]*\s*:\s*(.+)$",  // "Table 1.1: Description"
                @"^Table\s*:\s*(.+)$",                // "Table: Description"
                @"^Table\s+(.+)$",                    // "Table Description"
                @"^Table\s+of\s+(.+)$"                // "Table of Contents"
            };

            foreach (var cell in headerCells.Take(3)) {
                var content = cell.Content.Trim();
                foreach (var pattern in captionPatterns) {
                    var match = Regex.Match(content, pattern, RegexOptions.IgnoreCase);
                    if (match.Success) {
                        return match.Groups[1].Value.Trim();
                    }
                }
            }

            // If no caption pattern found, use first header cell content as fallback
            if (!string.IsNullOrWhiteSpace(headerCells[0].Content)) {
                var content = headerCells[0].Content.Trim();
                // Only use as caption if it's not a standard column name
                var standardColumns = new[] { "Item", "Description", "Value", "Unit", "Note", "Remark" };
                if (!standardColumns.Any(sc => content.Equals(sc, StringComparison.OrdinalIgnoreCase))) {
                    return content;
                }
            }
        }

        // Fallback: look for caption in first row of all cells
        var firstRowCells = table.Cells.Where(c => c.RowIndex == 0).OrderBy(c => c.ColumnIndex).ToArray();
        if (firstRowCells.Length > 0) {
            var combinedFirstRow = string.Join(" ", firstRowCells.Select(c => c.Content.Trim()));
            if (combinedFirstRow.Length < 100) // Only if reasonable length
            {
                return combinedFirstRow;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// T053/T054: Determines the section where a table is located
    /// Uses page context and table content heuristics
    /// </summary>
    private string DetermineTableSection(DocumentTable table, DocumentPage[] pages) {
        // Try to get section from the page where the table starts
        var startPage = pages.FirstOrDefault(p => p.PageNumber == table.StartPageNumber);
        if (startPage != null && !string.IsNullOrWhiteSpace(startPage.PrimarySection)) {
            return startPage.PrimarySection;
        }

        // Fallback: analyze table content to infer section
        var allCellText = string.Join(" ", table.Cells.Select(c => c.Content.ToUpperInvariant()));

        if (allCellText.Contains("specification") || allCellText.Contains("spec"))
            return "Specifications";
        if (allCellText.Contains("maintenance") || allCellText.Contains("service"))
            return "Maintenance";
        if (allCellText.Contains("torque"))
            return "Torque Specifications";
        if (allCellText.Contains("dimension"))
            return "Dimensions";
        if (allCellText.Contains("capacity"))
            return "Capacities";
        if (allCellText.Contains("wiring") || allCellText.Contains("circuit"))
            return "Electrical";
        if (allCellText.Contains("part") || allCellText.Contains("component"))
            return "Parts List";

        return "Table Data";
    }

    private async Task<List<string>> ProcessMultimodalContentAsync(PDFDocument input) {
        _logger.LogDebug("Processing multimodal content using GPT-4 Vision");

        var multimodalContent = new List<string>();

        try {
            using var memoryStream = new MemoryStream();
            await input.Content.CopyToAsync(memoryStream);
            var documentBytes = memoryStream.ToArray();

            var prompt = @"Analyze this motorcycle manual page and describe:
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
                "application/pdf",
                CancellationToken.None); // Added missing cancellation token

            multimodalContent.Add(visionAnalysis);

            _logger.LogDebug("GPT-4 Vision processing completed");
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "Failed to process multimodal content, continuing with text-only processing");
        }

        return multimodalContent;
    }

    private async Task<List<PDFChunk>> CreateSemanticChunksAsync(
        DocumentAnalysisResult analysisResult,
        List<string> multimodalContent,
        PDFDocument input) {
        _logger.LogDebug("Creating semantic chunks with embedding-based boundary detection");

        var chunks = new List<PDFChunk>();
        var chunkId = 0;

        // Process each page
        foreach (var page in analysisResult.Pages) {
            var pageChunks = await CreatePageChunksAsync(page, input, chunkId);
            chunks.AddRange(pageChunks);
            chunkId += pageChunks.Count;
        }

        // Process tables separately
        foreach (var table in analysisResult.Tables) {
            var tableChunk = CreateTableChunk(table, analysisResult.Pages, input, chunkId++);
            chunks.Add(tableChunk);
        }

        // Add multimodal content as separate chunks
        foreach (var content in multimodalContent) {
            var multimodalChunk = new PDFChunk {
                Id = $"{input.FileName}_multimodal_{chunkId++}",
                Content = content,
                PageNumber = 0, // Multimodal content spans multiple pages
                Section = "Visual Analysis",
                Type = ChunkType.Image,
                Metadata = new Dictionary<string, object> {
                    ["Source"] = "GPT-4 Vision",
                    ["ContentType"] = "Multimodal Analysis"
                }
            };
            chunks.Add(multimodalChunk);
        }

        // Apply semantic boundary detection
        if (_config.PreserveStructure) {
            chunks = await RefineChunkBoundariesAsync(chunks);
        }

        _logger.LogDebug("Created {ChunkCount} semantic chunks", chunks.Count);
        return chunks;
    }

    private async Task<List<PDFChunk>> CreatePageChunksAsync(DocumentPage page, PDFDocument input, int startingChunkId) {
        var chunks = new List<PDFChunk>();
        var content = page.Content;

        if (string.IsNullOrWhiteSpace(content))
            return chunks;

        // Detect sections using headers and structure
        var sections = DetectSections(content);
        var chunkId = startingChunkId;

        foreach (var section in sections) {
            // Split section into chunks based on size limits
            var sectionChunks = SplitTextIntoChunks(section.Content, _config.MaxChunkSize, _config.MinChunkSize, _config.ChunkOverlap);

            foreach (var chunkContent in sectionChunks) {
                // Determine the section title to use - prioritize detected section, fallback to page metadata
                var sectionTitle = !string.IsNullOrEmpty(section.Title) ? section.Title : page.PrimarySection;

                // Extract nested ternary operations into independent statements
                int sectionLevel;
                if (page.SectionLevel > 0)
                {
                    sectionLevel = page.SectionLevel;
                }
                else
                {
                    sectionLevel = section.Level > 0 ? section.Level : 0;
                }

                string[] allSectionHeadings;
                if (page.SectionHeadings != null && page.SectionHeadings.Length > 0)
                {
                    allSectionHeadings = page.SectionHeadings;
                }
                else
                {
                    allSectionHeadings = section.Level > 0 && sectionTitle != null ? new[] { sectionTitle } : Array.Empty<string>();
                }

                var chunk = new PDFChunk {
                    Id = $"{input.FileName}_page_{page.PageNumber}_chunk_{chunkId++}",
                    Content = chunkContent,
                    PageNumber = page.PageNumber,
                    Section = sectionTitle ?? string.Empty,
                    Type = ChunkType.Text,
                    Metadata = new Dictionary<string, object> {
                        ["PageWidth"] = page.Width,
                        ["PageHeight"] = page.Height,
                        ["SectionType"] = section.Type,
                        // T053: Add page/section metadata for locator tracking
                        // Use pre-enriched metadata if available, otherwise use detected section
                        ["PageNumber"] = page.PageNumber,
                        ["PageRange"] = $"{page.PageNumber}-{page.PageNumber}",
                        ["PrimarySection"] = !string.IsNullOrEmpty(page.PrimarySection) ? page.PrimarySection : (sectionTitle ?? string.Empty),
                        ["SectionLevel"] = sectionLevel,
                        ["AllSectionHeadings"] = allSectionHeadings,
                        ["SectionTitle"] = sectionTitle ?? string.Empty,
                        ["ChunkIndex"] = chunkId - startingChunkId - 1
                    }
                };
                chunks.Add(chunk);
            }
        }

        return chunks;
    }

    private PDFChunk CreateTableChunk(DocumentTable table, DocumentPage[] pages, PDFDocument input, int chunkId) {
        var tableContent = new StringBuilder();

        // T054: Preserve table structure - include caption and structured format
        if (!string.IsNullOrEmpty(table.Caption)) {
            tableContent.AppendLine($"Table: {table.Caption}");
            tableContent.AppendLine();
        }

        tableContent.AppendLine($"Table with {table.RowCount} rows and {table.ColumnCount} columns:");
        tableContent.AppendLine();

        // T054: Convert table to structured text format preserving rows/columns
        var rows = table.Cells.GroupBy(c => c.RowIndex).OrderBy(g => g.Key);
        foreach (var row in rows) {
            var cells = row.OrderBy(c => c.ColumnIndex).Select(c => c.Content);
            tableContent.AppendLine(string.Join(" | ", cells));

            // Add separator line after header row for readability
            if (row.Any(c => c.IsHeader)) {
                var separator = string.Join("-+-", cells.Select(_ => new string('-', 20)));
                tableContent.AppendLine(separator);
            }
        }

        // T053: Determine page range for table
        var pageRange = table.StartPageNumber == table.EndPageNumber
            ? $"{table.StartPageNumber}"
            : $"{table.StartPageNumber}-{table.EndPageNumber}";

        // T053: Use table section or fallback to "Table Data"
        var section = !string.IsNullOrEmpty(table.Section) ? table.Section : "Table Data";

        // Best-effort: inherit section hierarchy from the page where the table starts
        var startPage = pages.FirstOrDefault(p => p.PageNumber == table.StartPageNumber);
        var sectionLevel = startPage?.SectionLevel ?? 0;
        var sectionHeadings = startPage?.SectionHeadings ?? Array.Empty<string>();

        return new PDFChunk {
            Id = $"{input.FileName}_table_{chunkId}",
            Content = tableContent.ToString(),
            PageNumber = table.StartPageNumber, // Use start page as primary page
            Section = section,
            Type = ChunkType.Table,
            Metadata = new Dictionary<string, object> {
                ["RowCount"] = table.RowCount,
                ["ColumnCount"] = table.ColumnCount,
                ["CellCount"] = table.Cells.Length,
                // T053: Add page/section locator metadata for tables
                ["PageNumber"] = table.StartPageNumber,
                ["PageRange"] = pageRange,
                ["EndPageNumber"] = table.EndPageNumber,
                ["Section"] = section,
                ["SectionLevel"] = sectionLevel,
                ["AllSectionHeadings"] = sectionHeadings,
                ["TableCaption"] = table.Caption,
                ["IsMultiPageTable"] = table.StartPageNumber != table.EndPageNumber
            }
        };
    }

    /// <summary>
    /// T053: Detects sections with hierarchy level tracking for locator metadata
    /// </summary>
    private List<DocumentSection> DetectSections(string content) {
        var sections = new List<DocumentSection>();

        // T053: Enhanced section detection with hierarchy levels for motorcycle manuals
        var headerPatterns = new[]
        {
            new { Pattern = @"^(CHAPTER\s+\d+.*)$", Level = 1, Name = "Chapter" },
            new { Pattern = @"^(\d+\.\s+.+)$", Level = 2, Name = "Section" }, // Numbered sections (1. Introduction)
            new { Pattern = @"^([A-Z][A-Z\s]+)$", Level = 2, Name = "Header" }, // ALL CAPS headers
            new { Pattern = @"^([A-Z][a-z\s]+):$", Level = 3, Name = "Subsection" }, // Title case with colon
            new { Pattern = @"^(SECTION\s+\d+.*)$", Level = 2, Name = "Section" } // Section headers
        };

        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        DocumentSection? currentSection = null;

        foreach (var line in lines) {
            var matchedHeader = headerPatterns
                .Select(headerDef => new { HeaderDef = headerDef, Match = Regex.Match(line.Trim(), headerDef.Pattern, RegexOptions.Multiline) })
                .FirstOrDefault(result => result.Match.Success);

            if (matchedHeader != null) {
                // Save current section
                if (currentSection != null && currentSection.Content.Length > 0) {
                    sections.Add(currentSection);
                }

                // Start new section with hierarchy level
                currentSection = new DocumentSection {
                    Title = matchedHeader.Match.Groups[1].Value.Trim(),
                    Content = new StringBuilder(),
                    Type = DetermineContentType(matchedHeader.Match.Groups[1].Value),
                    Level = matchedHeader.HeaderDef.Level
                };
            }
            else {
                // If no section has been started yet, create a default "General Content" section
                currentSection ??= new DocumentSection {
                    Title = "General Content",
                    Content = new StringBuilder(),
                    Type = "General",
                    Level = 0
                };
                currentSection.Content.AppendLine(line);
            }
        }

        // Add the last section
        if (currentSection != null && currentSection.Content.Length > 0) {
            sections.Add(currentSection);
        }

        return sections;
    }

    /// <summary>
    /// T053: Extracts section headings and hierarchy from page content
    /// Returns primary section, all headings, and hierarchy level
    /// </summary>
    private (string primarySection, string[] headings, int level) ExtractSectionMetadata(string content) {
        var headings = new List<string>();
        var primarySection = string.Empty;
        var maxLevel = int.MaxValue;

        // Header patterns with hierarchy levels
        var headerPatterns = new[]
        {
            new { Pattern = @"^(CHAPTER\s+\d+.*)$", Level = 1 },
            new { Pattern = @"^(\d+\.\s+.+)$", Level = 2 },
            new { Pattern = @"^([A-Z][A-Z\s]+)$", Level = 2 },
            new { Pattern = @"^([A-Z][a-z\s]+):$", Level = 3 },
            new { Pattern = @"^(SECTION\s+\d+.*)$", Level = 2 }
        };

        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines) {
            var matchedHeader = headerPatterns
                .Select(headerDef => new { HeaderDef = headerDef, Match = Regex.Match(line.Trim(), headerDef.Pattern, RegexOptions.Multiline) })
                .FirstOrDefault(result => result.Match.Success);

            if (matchedHeader != null) {
                var heading = matchedHeader.Match.Groups[1].Value.Trim();
                headings.Add(heading);

                // Track primary section (highest level heading)
                if (string.IsNullOrEmpty(primarySection) || matchedHeader.HeaderDef.Level < maxLevel) {
                    primarySection = heading;
                    maxLevel = matchedHeader.HeaderDef.Level;
                }
            }
        }

        // Fallback if no headings found
        if (string.IsNullOrEmpty(primarySection)) {
            primarySection = "General Content";
        }

        return (primarySection, headings.ToArray(), maxLevel == int.MaxValue ? 0 : maxLevel);
    }

    private string DetermineContentType(string sectionTitle) {
        var title = sectionTitle.ToUpperInvariant();

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

    private List<string> SplitTextIntoChunks(StringBuilder content, int maxChunkSize, int minChunkSize, int overlap) {
        var text = content.ToString();
        var chunks = new List<string>();

        if (text.Length <= maxChunkSize) {
            chunks.Add(text);
            return chunks;
        }

        var sentences = text.Split(SentenceEndCharacters, StringSplitOptions.RemoveEmptyEntries);
        var currentChunk = new StringBuilder();

        foreach (var sentence in sentences) {
            var trimmedSentence = sentence.Trim();
            if (string.IsNullOrEmpty(trimmedSentence)) continue;

            // Check if adding this sentence would exceed the max chunk size
            if (currentChunk.Length + trimmedSentence.Length + 1 > maxChunkSize && currentChunk.Length >= minChunkSize) {
                chunks.Add(currentChunk.ToString().Trim());

                // Start new chunk with overlap
                var overlapText = GetOverlapText(currentChunk.ToString(), overlap);
                currentChunk = new StringBuilder(overlapText);
            }

            currentChunk.Append(trimmedSentence).Append(". ");
        }

        // Add the last chunk
        if (currentChunk.Length >= minChunkSize) {
            chunks.Add(currentChunk.ToString().Trim());
        }

        return chunks;
    }

    private string GetOverlapText(string text, int overlapSize) {
        if (text.Length <= overlapSize) return text;

        var startIndex = text.Length - overlapSize;
        var overlapText = text.Substring(startIndex);

        // Try to start at a sentence boundary
        var sentenceStart = overlapText.IndexOf(". ");
        if (sentenceStart > 0 && sentenceStart < overlapSize / 2) {
            overlapText = overlapText.Substring(sentenceStart + 2);
        }

        return overlapText;
    }

    private float CalculateCosineSimilarity(float[] vectorA, float[] vectorB) {
        if (vectorA.Length != vectorB.Length)
            throw new ArgumentException("Vectors must have the same length");

        var dotProduct = vectorA.Zip(vectorB, (a, b) => a * b).Sum();
        var magnitudeA = Math.Sqrt(vectorA.Sum(a => a * a));
        var magnitudeB = Math.Sqrt(vectorB.Sum(b => b * b));

        if (magnitudeA < float.Epsilon || magnitudeB < float.Epsilon)
            return 0;

        return (float)(dotProduct / (magnitudeA * magnitudeB));
    }

    private async Task GenerateEmbeddingsAsync(List<PDFChunk> chunks) {
        _logger.LogDebug("Generating embeddings for {ChunkCount} chunks", chunks.Count);

        // Process chunks in batches for efficiency
        const int batchSize = 10;
        var batches = chunks.Chunk(batchSize);

        foreach (var batch in batches) {
            var batchArray = batch.ToArray();
            var texts = batchArray.Select(c => c.Content).ToArray();
            var embeddings = await _openAIClient.GetEmbeddingsAsync(_azureConfig.Models.EmbeddingModel, texts, CancellationToken.None);

            for (int i = 0; i < batchArray.Length && i < embeddings.Length; i++) {
                batchArray[i].Embedding = embeddings[i];
            }
        }

        _logger.LogDebug("Embedding generation completed");
    }

    private async Task<List<PDFChunk>> RefineChunkBoundariesAsync(List<PDFChunk> chunks) {
        _logger.LogDebug("Refining chunk boundaries using embedding-based similarity");

        // For chunks that are too similar, merge them
        // For chunks that are too different, consider splitting them further
        var refinedChunks = new List<PDFChunk>();

        int i = 0;
        while (i < chunks.Count) {
            var currentChunk = chunks[i];

            // Check similarity with next chunk if it exists
            if (i < chunks.Count - 1) {
                var nextChunk = chunks[i + 1];

                // Generate embeddings for similarity comparison
                var embeddings = await _openAIClient.GetEmbeddingsAsync(
                    _azureConfig.Models.EmbeddingModel,
                    new[] { currentChunk.Content, nextChunk.Content },
                    CancellationToken.None);

                if (embeddings.Length < 2) {
                    _logger.LogWarning("Failed to generate embeddings for similarity comparison between chunks {CurrentId} and {NextId}",
                        LogSanitizer.Sanitize(currentChunk.Id), LogSanitizer.Sanitize(nextChunk.Id));
                    refinedChunks.Add(currentChunk);
                    i++;
                    continue;
                }

                var similarity = CalculateCosineSimilarity(embeddings[0], embeddings[1]);

                // If chunks are very similar and from the same section, consider merging
                if (similarity > _config.SimilarityThreshold &&
                    currentChunk.Section == nextChunk.Section &&
                    currentChunk.Content.Length + nextChunk.Content.Length <= _config.MaxChunkSize) {
                    // Merge chunks
                    var mergedChunk = new PDFChunk {
                        Id = $"{currentChunk.Id}_merged",
                        Content = $"{currentChunk.Content}\n\n{nextChunk.Content}",
                        PageNumber = currentChunk.PageNumber,
                        Section = currentChunk.Section,
                        Type = currentChunk.Type,
                        Metadata = currentChunk.Metadata
                    };

                    refinedChunks.Add(mergedChunk);
                    i += 2; // Skip the next chunk because it's merged
                    continue;
                }

                // Not merged - keep current chunk
                refinedChunks.Add(currentChunk);
                i++;
            }
            else {
                refinedChunks.Add(currentChunk);
                i++;
            }
        }

        _logger.LogDebug("Refined {OriginalCount} chunks to {RefinedCount} chunks", chunks.Count, refinedChunks.Count);
        return refinedChunks;
    }

    /// <summary>
    /// T053/T054: Creates MotorcycleDocument objects with locator metadata for citation support
    /// T055: Populates top-level locator fields for indexing
    /// </summary>
    private async Task<List<MotorcycleDocument>> CreateMotorcycleDocumentsAsync(
        List<PDFChunk> chunks,
        PDFDocument input) {
        _logger.LogDebug("Creating MotorcycleDocument objects from chunks with locator metadata");

        var documents = new List<MotorcycleDocument>();

        foreach (var chunk in chunks) {
            // T053/T055: Extract locator metadata from chunk metadata for citation support with defensive type checking
            int pageNumber;
            try {
                pageNumber = chunk.Metadata.TryGetValue("PageNumber", out var pageNumberObj)
                    ? Convert.ToInt32(pageNumberObj)
                    : chunk.PageNumber;
            }
            catch (Exception ex) {
                _logger.LogWarning(ex, "Failed to parse PageNumber from metadata for chunk {ChunkId}, using fallback",
                    LogSanitizer.Sanitize(chunk.Id));
                pageNumber = chunk.PageNumber;
            }

            var pageRange = chunk.Metadata.TryGetValue("PageRange", out var pageRangeObj) ? pageRangeObj?.ToString() : $"{pageNumber}";
            var primarySection = chunk.Metadata.TryGetValue("PrimarySection", out var primarySectionObj) ? primarySectionObj?.ToString() : chunk.Section;

            int sectionLevel;
            try {
                sectionLevel = chunk.Metadata.TryGetValue("SectionLevel", out var sectionLevelObj) ? Convert.ToInt32(sectionLevelObj) : 0;
            }
            catch (Exception ex) {
                _logger.LogWarning(ex, "Failed to parse SectionLevel from metadata for chunk {ChunkId}, using default 0",
                    LogSanitizer.Sanitize(chunk.Id));
                sectionLevel = 0;
            }

            string[] sectionHeadings;
            try {
                sectionHeadings = chunk.Metadata.TryGetValue("AllSectionHeadings", out var headingsObj) ? (string[])headingsObj : Array.Empty<string>();
            }
            catch (Exception ex) {
                _logger.LogWarning(ex, "Failed to parse AllSectionHeadings from metadata for chunk {ChunkId}, using empty array",
                    LogSanitizer.Sanitize(chunk.Id));
                sectionHeadings = Array.Empty<string>();
            }

            var tableCaption = chunk.Metadata.TryGetValue("TableCaption", out var tableCaptionObj) ? tableCaptionObj?.ToString() : null;

            int chunkIndex;
            try {
                chunkIndex = chunk.Metadata.TryGetValue("ChunkIndex", out var chunkIndexObj) ? Convert.ToInt32(chunkIndexObj) : 0;
            }
            catch (Exception ex) {
                _logger.LogWarning(ex, "Failed to parse ChunkIndex from metadata for chunk {ChunkId}, using default 0",
                    LogSanitizer.Sanitize(chunk.Id));
                chunkIndex = 0;
            }

            bool isMultiPageTable;
            try {
                isMultiPageTable = chunk.Metadata.TryGetValue("IsMultiPageTable", out var isMultiPageObj) && (bool)isMultiPageObj;
            }
            catch (Exception ex) {
                _logger.LogWarning(ex, "Failed to parse IsMultiPageTable from metadata for chunk {ChunkId}, using default false",
                    LogSanitizer.Sanitize(chunk.Id));
                isMultiPageTable = false;
            }

            // build DocumentMetadata by mutating getter-only collections
            var dm = new DocumentMetadata();
            dm.SourceFile = input.FileName;
            // convert source string to Uri safely
            if (!string.IsNullOrWhiteSpace(input.Source)) {
                try { dm.SourceUrl = new Uri(input.Source); } catch { dm.SourceUrl = null; }
            }
            dm.PageNumber = pageNumber;
            dm.Section = chunk.Section;
            dm.Author = $"{input.Make} {input.Model}";
            dm.PublishedDate = input.UploadedAt;
            // add tags
            dm.Tags.Add(input.Make);
            dm.Tags.Add(input.Model);
            dm.Tags.Add(input.Year);
            dm.Tags.Add(input.DocumentType.ToString());
            // additional properties
            dm.AdditionalProperties["SourceType"] = "PDF";
            dm.AdditionalProperties["Make"] = input.Make;
            dm.AdditionalProperties["Model"] = input.Model;
            dm.AdditionalProperties["Year"] = input.Year;
            dm.AdditionalProperties["DocumentType"] = input.DocumentType.ToString();
            dm.AdditionalProperties["ChunkType"] = chunk.Type.ToString();
            dm.AdditionalProperties["ProcessedAt"] = DateTime.UtcNow;
            dm.AdditionalProperties["Language"] = input.Language;
            dm.AdditionalProperties["ChunkMetadata"] = chunk.Metadata;
            dm.AdditionalProperties["IsMultiPageTable"] = isMultiPageTable;
            dm.AdditionalProperties["Locator"] = new {
                PageNumber = pageNumber,
                PageRange = pageRange,
                Section = chunk.Section,
                PrimarySection = primarySection,
                SectionLevel = sectionLevel,
                SectionHeadings = sectionHeadings,
                TableCaption = tableCaption,
                ChunkIndex = chunkIndex
            };

            var document = new MotorcycleDocument {
                Id = chunk.Id,
                Title = $"{input.Make} {input.Model} {input.Year} - {chunk.Section}",
                Content = chunk.Content,
                Type = DocumentType.Manual,
                ContentVector = chunk.Embedding,
                // T055: Populate top-level locator fields for indexing
                PageNumber = pageNumber,
                PageRange = pageRange,
                PrimarySection = primarySection,
                SectionLevel = sectionLevel,
                TableCaption = tableCaption,
                ChunkIndex = chunkIndex,
                Metadata = dm
            };

            // Add section headings to the read-only collection
            foreach (var heading in sectionHeadings) {
                document.SectionHeadings.Add(heading);
            }

            documents.Add(document);
        }

        _logger.LogDebug("Created {DocumentCount} MotorcycleDocument objects with locator metadata", documents.Count);
        return documents;
    }

    private Dictionary<string, object> CreateProcessingMetadata(
        PDFDocument input,
        DocumentAnalysisResult analysisResult,
        int chunkCount) {
        return new Dictionary<string, object> {
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
            ["ProcessingConfiguration"] = new {
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

    /// <summary>
    /// T053: Internal class for tracking detected sections with hierarchy level
    /// </summary>
    private class DocumentSection {
        public string Title { get; set; } = string.Empty;
        public StringBuilder Content { get; set; } = new();
        public string Type { get; set; } = string.Empty;
        /// <summary>
        /// Hierarchy level: 1=Chapter, 2=Section, 3=Subsection
        /// </summary>
        public int Level { get; set; }
    }
}
