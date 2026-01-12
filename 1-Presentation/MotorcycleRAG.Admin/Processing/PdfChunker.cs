using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using System.Text;

namespace MotorcycleRAG.Admin.Processing;

/// <summary>
/// Result of PDF chunking operation
/// </summary>
internal class PdfChunkingResult
{
    private readonly List<DocumentChunk> _chunks = new();
    private readonly List<string> _errors = new();
    private readonly List<string> _warnings = new();

    internal bool Success { get; set; }
    internal IReadOnlyList<DocumentChunk> Chunks => _chunks.AsReadOnly();
    internal PdfMetadata Metadata { get; set; } = new();
    internal IReadOnlyList<string> Errors => _errors.AsReadOnly();
    internal IReadOnlyList<string> Warnings => _warnings.AsReadOnly();

    // Internal methods for modification
    internal void AddChunk(DocumentChunk chunk) => _chunks.Add(chunk);
    internal void AddError(string error) => _errors.Add(error);
    internal void AddWarning(string warning) => _warnings.Add(warning);
}

/// <summary>
/// Metadata extracted from PDF
/// </summary>
internal class PdfMetadata
{
    internal string? Title { get; set; }
    internal string? Author { get; set; }
    internal int PageCount { get; set; }
    internal DateTime? CreationDate { get; set; }
    internal string? Producer { get; set; }
}

/// <summary>
/// A chunk of document text with metadata
/// </summary>
internal class DocumentChunk
{
    internal int ChunkIndex { get; set; }
    internal string Text { get; set; } = string.Empty;
    internal int PageNumber { get; set; }
    internal string? Section { get; set; }
    internal Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Service for chunking PDF documents into searchable segments.
/// Implements domain-specific logic for parsing motorcycle manuals.
/// </summary>
public class PdfChunker {
    private static readonly string[] LineSeparators = { "\r\n", "\n", "\r" };
    private readonly int _targetChunkSize;
    private readonly int _chunkOverlap;

    public PdfChunker()
        : this(targetChunkSize: 1000, chunkOverlap: 200)
    {
    }

    public PdfChunker(int targetChunkSize)
        : this(targetChunkSize, chunkOverlap: 200)
    {
    }

    public PdfChunker(int targetChunkSize, int chunkOverlap)
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
    internal async Task<PdfChunkingResult> ProcessPdfAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var result = new PdfChunkingResult();

        try
        {
            // Input validation
            if (string.IsNullOrWhiteSpace(filePath))
            {
                result.AddError("File path is required.");
                return result;
            }

            if (!File.Exists(filePath))
            {
                result.AddError($"File not found: {filePath}");
                return result;
            }

            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            if (extension != ".pdf")
            {
                result.AddError("Invalid file type. Only PDF files are supported.");
                return result;
            }

            var fileInfo = new FileInfo(filePath);
            const long maxSizeBytes = 100 * 1024 * 1024; // 100 MB
            if (fileInfo.Length > maxSizeBytes)
            {
                result.AddError($"File too large. Max allowed size is 100MB. Actual size: {fileInfo.Length / (1024 * 1024)}MB");
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
                foreach (var chunk in CreateChunks(pageTexts, sections))
                {
                    result.AddChunk(chunk);
                }

                result.Success = true;
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            result.AddError("Processing was cancelled");
        }
        catch (Exception ex)
        {
            result.AddError($"Error processing PDF: {ex.Message}");
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
            var lines = text.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);
            
            // Find first section header in the page
            var sectionHeader = lines
                .Select(line => line.Trim())
                .FirstOrDefault(trimmed =>
                    trimmed.Length > 5 &&
                    trimmed.Length < 100 &&
                    IsLikelySectionHeader(trimmed));

            if (sectionHeader != null)
            {
                currentSection = sectionHeader;
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
            {
                return true;
            }
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
                    ChunkIndex = chunks.Count,
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
                ChunkIndex = chunks.Count,
                Text = buffer.ToString(),
                PageNumber = bufferPageStart,
                Section = bufferSection,
                Metadata = new Dictionary<string, object>
                {
                    ["pageNumber"] = bufferPageStart,
                    ["pageRange"] = $"{bufferPageStart}-{pageTexts[^1].PageNumber}",
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


