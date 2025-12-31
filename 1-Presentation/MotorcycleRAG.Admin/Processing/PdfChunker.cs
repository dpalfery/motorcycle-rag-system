using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using System.Text;

namespace MotorcycleRAG.Admin.Processing;

/// <summary>
/// Result of PDF chunking operation
/// </summary>
public class PdfChunkingResult
{
    public bool Success { get; set; }
    public List<DocumentChunk> Chunks { get; set; } = new();
    public PdfMetadata Metadata { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Metadata extracted from PDF
/// </summary>
public class PdfMetadata
{
    public string? Title { get; set; }
    public string? Author { get; set; }
    public int PageCount { get; set; }
    public DateTime? CreationDate { get; set; }
    public string? Producer { get; set; }
}

/// <summary>
/// A chunk of document text with metadata
/// </summary>
public class DocumentChunk
{
    public int ChunkIndex { get; set; }
    public string Text { get; set; } = string.Empty;
    public int PageNumber { get; set; }
    public string? Section { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Service for chunking PDF documents with page and section metadata extraction
/// </summary>
public class PdfChunker
{
    private readonly int _targetChunkSize;
    private readonly int _chunkOverlap;

    public PdfChunker(int targetChunkSize = 1000, int chunkOverlap = 200)
    {
        if (targetChunkSize <= 0)
            throw new ArgumentException("Target chunk size must be positive", nameof(targetChunkSize));
        if (chunkOverlap < 0)
            throw new ArgumentException("Chunk overlap cannot be negative", nameof(chunkOverlap));
        if (chunkOverlap >= targetChunkSize)
            throw new ArgumentException("Chunk overlap must be less than target chunk size", nameof(chunkOverlap));

        _targetChunkSize = targetChunkSize;
        _chunkOverlap = chunkOverlap;
    }

    /// <summary>
    /// Processes a PDF file and extracts chunks with metadata
    /// </summary>
    public async Task<PdfChunkingResult> ProcessPdfAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var result = new PdfChunkingResult();

        try
        {
            // Input validation
            if (string.IsNullOrWhiteSpace(filePath))
            {
                result.Errors.Add("File path is required.");
                return result;
            }

            if (!File.Exists(filePath))
            {
                result.Errors.Add($"File not found: {filePath}");
                return result;
            }

            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            if (extension != ".pdf")
            {
                result.Errors.Add("Invalid file type. Only PDF files are supported.");
                return result;
            }

            var fileInfo = new FileInfo(filePath);
            const long maxSizeBytes = 100 * 1024 * 1024; // 100 MB
            if (fileInfo.Length > maxSizeBytes)
            {
                result.Errors.Add($"File too large. Max allowed size is 100MB. Actual size: {fileInfo.Length / (1024 * 1024)}MB");
                return result;
            }

            // Process synchronously but allow cancellation
            await Task.Run(() =>
            {
                using var document = PdfDocument.Open(filePath);

                // Extract metadata
                result.Metadata = ExtractMetadata(document);

                // Process each page
                var allText = new StringBuilder();
                var pageTexts = new List<(int PageNumber, string Text)>();

                foreach (var page in document.GetPages())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var pageText = ExtractPageText(page);
                    pageTexts.Add((page.Number, pageText));
                    allText.AppendLine(pageText);
                }

                // Detect sections (basic heuristic: lines starting with numbers or uppercase headers)
                var sections = DetectSections(pageTexts);

                // Create chunks with page and section metadata
                result.Chunks = CreateChunks(pageTexts, sections);

                result.Success = true;
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            result.Errors.Add("Processing was cancelled");
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Error processing PDF: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Extracts metadata from PDF document
    /// </summary>
    private PdfMetadata ExtractMetadata(PdfDocument document)
    {
        var metadata = new PdfMetadata
        {
            PageCount = document.NumberOfPages
        };

        try
        {
            var info = document.Information;
            metadata.Title = !string.IsNullOrEmpty(info.Title) ? info.Title : null;
            metadata.Author = !string.IsNullOrEmpty(info.Author) ? info.Author : null;
            metadata.Producer = !string.IsNullOrEmpty(info.Producer) ? info.Producer : null;
            
            if (!string.IsNullOrEmpty(info.CreationDate) && DateTime.TryParse(info.CreationDate, out var creationDate))
            {
                metadata.CreationDate = creationDate;
            }
        }
        catch
        {
            // Metadata extraction is best-effort
        }

        return metadata;
    }

    /// <summary>
    /// Extracts text from a PDF page
    /// </summary>
    private string ExtractPageText(UglyToad.PdfPig.Content.Page page)
    {
        try
        {
            var text = page.Text;
            
            // Basic cleanup: normalize whitespace
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
            text = text.Trim();
            
            return text;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Detects sections in the document based on text patterns
    /// </summary>
    private Dictionary<int, string> DetectSections(List<(int PageNumber, string Text)> pageTexts)
    {
        var sections = new Dictionary<int, string>();
        string currentSection = "Introduction";

        foreach (var (pageNumber, text) in pageTexts)
        {
            // Look for section headers (simple heuristic: short lines in all caps or numbered)
            var lines = text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                
                // Detect section headers: all caps, short, or numbered
                if (trimmed.Length > 5 && trimmed.Length < 100)
                {
                    if (IsLikelySectionHeader(trimmed))
                    {
                        currentSection = trimmed;
                        break;
                    }
                }
            }
            
            sections[pageNumber] = currentSection;
        }

        return sections;
    }

    /// <summary>
    /// Heuristic to detect if a line is likely a section header
    /// </summary>
    private bool IsLikelySectionHeader(string line)
    {
        // Check if line starts with a number (e.g., "1.2 Engine Specifications")
        if (char.IsDigit(line[0]))
            return true;

        // Check if mostly uppercase (at least 70% uppercase letters)
        var letters = line.Count(char.IsLetter);
        if (letters > 0)
        {
            var upperCount = line.Count(char.IsUpper);
            if ((double)upperCount / letters >= 0.7)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Creates chunks from page texts with metadata
    /// </summary>
    private List<DocumentChunk> CreateChunks(
        List<(int PageNumber, string Text)> pageTexts,
        Dictionary<int, string> sections)
    {
        var chunks = new List<DocumentChunk>();
        var chunkIndex = 0;
        var buffer = new StringBuilder();
        var bufferPageStart = 1;
        var bufferSection = sections.GetValueOrDefault(1, "Unknown");

        foreach (var (pageNumber, pageText) in pageTexts)
        {
            var currentSection = sections.GetValueOrDefault(pageNumber, bufferSection);
            
            // Add page text to buffer
            if (buffer.Length > 0)
                buffer.Append(' ');
            buffer.Append(pageText);

            // Check if we should create a chunk
            while (buffer.Length >= _targetChunkSize)
            {
                var chunkText = ExtractChunk(buffer);
                
                chunks.Add(new DocumentChunk
                {
                    ChunkIndex = chunkIndex++,
                    Text = chunkText,
                    PageNumber = bufferPageStart,
                    Section = bufferSection,
                    Metadata = new Dictionary<string, object>
                    {
                        ["pageNumber"] = bufferPageStart,
                        ["pageRange"] = $"{bufferPageStart}-{pageNumber}",
                        ["section"] = bufferSection ?? "Unknown",
                        ["chunkSize"] = chunkText.Length
                    }
                });

                // Update buffer tracking
                bufferPageStart = pageNumber;
                bufferSection = currentSection;
            }
        }

        // Create final chunk from remaining buffer
        if (buffer.Length > 0)
        {
            chunks.Add(new DocumentChunk
            {
                ChunkIndex = chunkIndex++,
                Text = buffer.ToString(),
                PageNumber = bufferPageStart,
                Section = bufferSection,
                Metadata = new Dictionary<string, object>
                {
                    ["pageNumber"] = bufferPageStart,
                    ["pageRange"] = $"{bufferPageStart}-{pageTexts.Last().PageNumber}",
                    ["section"] = bufferSection ?? "Unknown",
                    ["chunkSize"] = buffer.Length
                }
            });
        }

        return chunks;
    }

    /// <summary>
    /// Extracts a chunk from the buffer with overlap handling
    /// </summary>
    private string ExtractChunk(StringBuilder buffer)
    {
        var chunkLength = Math.Min(_targetChunkSize, buffer.Length);
        
        // Try to break at sentence boundary
        var text = buffer.ToString(0, chunkLength);
        var lastPeriod = text.LastIndexOf('.');
        
        if (lastPeriod > _targetChunkSize / 2)
        {
            chunkLength = lastPeriod + 1;
            text = buffer.ToString(0, chunkLength);
        }

        // Remove from buffer, keeping overlap
        var removeLength = Math.Max(0, chunkLength - _chunkOverlap);
        buffer.Remove(0, removeLength);

        return text.Trim();
    }
}
