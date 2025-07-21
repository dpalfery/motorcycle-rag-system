using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Core.Models;

/// <summary>
/// Represents a PDF document for processing motorcycle manuals and documentation
/// </summary>
public class PDFDocument
{
    [Required]
    public string FileName { get; set; } = string.Empty;

    [Required]
    public Stream Content { get; set; } = Stream.Null;

    /// <summary>
    /// Document type (manual, specification, etc.)
    /// </summary>
    public PDFDocumentType DocumentType { get; set; } = PDFDocumentType.Manual;

    /// <summary>
    /// Language of the document
    /// </summary>
    public string Language { get; set; } = "en";

    /// <summary>
    /// Motorcycle make/brand this document relates to
    /// </summary>
    public string Make { get; set; } = string.Empty;

    /// <summary>
    /// Motorcycle model this document relates to
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Year or year range this document covers
    /// </summary>
    public string Year { get; set; } = string.Empty;

    /// <summary>
    /// Additional metadata about the PDF document
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>
    /// Source information for tracking
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Upload timestamp
    /// </summary>
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// File size in bytes
    /// </summary>
    public long FileSizeBytes { get; set; }

    /// <summary>
    /// Whether the document contains images/diagrams
    /// </summary>
    public bool ContainsImages { get; set; } = true;
}

/// <summary>
/// Types of PDF documents in the motorcycle domain
/// </summary>
public enum PDFDocumentType
{
    Manual,
    ServiceGuide,
    PartsManual,
    OwnerManual,
    TechnicalSpecification,
    RepairGuide,
    Other
}

/// <summary>
/// Configuration for PDF processing
/// </summary>
public class PDFProcessingConfiguration
{
    /// <summary>
    /// Maximum chunk size in characters for semantic chunking
    /// </summary>
    public int MaxChunkSize { get; set; } = 2000;

    /// <summary>
    /// Minimum chunk size in characters
    /// </summary>
    public int MinChunkSize { get; set; } = 200;

    /// <summary>
    /// Overlap between chunks in characters
    /// </summary>
    public int ChunkOverlap { get; set; } = 200;

    /// <summary>
    /// Similarity threshold for embedding-based boundary detection
    /// </summary>
    public float SimilarityThreshold { get; set; } = 0.7f;

    /// <summary>
    /// Whether to process images using GPT-4 Vision
    /// </summary>
    public bool ProcessImages { get; set; } = true;

    /// <summary>
    /// Whether to preserve document structure (sections, pages)
    /// </summary>
    public bool PreserveStructure { get; set; } = true;

    /// <summary>
    /// Maximum number of pages to process
    /// </summary>
    public int MaxPages { get; set; } = 500;

    /// <summary>
    /// Whether to extract and process tables
    /// </summary>
    public bool ProcessTables { get; set; } = true;
}

/// <summary>
/// Represents a semantic chunk from PDF processing
/// </summary>
public class PDFChunk
{
    public string Id { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int PageNumber { get; set; }
    public string Section { get; set; } = string.Empty;
    public ChunkType Type { get; set; } = ChunkType.Text;
    public float[] Embedding { get; set; } = Array.Empty<float>();
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Types of content chunks
/// </summary>
public enum ChunkType
{
    Text,
    Table,
    Image,
    Diagram,
    List,
    Header
}