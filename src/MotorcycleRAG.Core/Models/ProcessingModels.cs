using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Core.Models;

/// <summary>
/// Result of data processing operations
/// </summary>
public class ProcessingResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public ProcessedData? Data { get; set; }
    public List<string> Errors { get; set; } = new();
    public TimeSpan ProcessingTime { get; set; }
    public int ItemsProcessed { get; set; }
}

/// <summary>
/// Processed data ready for indexing
/// </summary>
public class ProcessedData
{
    [Required]
    public string Id { get; set; } = string.Empty;

    [Required]
    public List<MotorcycleDocument> Documents { get; set; } = new();

    public Dictionary<string, object> Metadata { get; set; } = new();
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Result of indexing operations
/// </summary>
public class IndexingResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int DocumentsIndexed { get; set; }
    public List<string> Errors { get; set; } = new();
    public TimeSpan IndexingTime { get; set; }
    public string IndexName { get; set; } = string.Empty;
}

/// <summary>
/// Search options for configuring search behavior
/// </summary>
public class SearchOptions
{
    public int MaxResults { get; set; } = 10;
    public float MinRelevanceScore { get; set; } = 0.5f;
    public bool IncludeMetadata { get; set; } = true;
    public Dictionary<string, object> Filters { get; set; } = new();
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    public bool EnableCaching { get; set; } = true;
}

/// <summary>
/// Search context for agent coordination
/// </summary>
public class SearchContext
{
    public string SessionId { get; set; } = string.Empty;
    public SearchPreferences Preferences { get; set; } = new();
    public QueryContext QueryContext { get; set; } = new();
    public Dictionary<string, object> AdditionalContext { get; set; } = new();
}

/// <summary>
/// Health check result
/// </summary>
public class HealthCheckResult
{
    public bool IsHealthy { get; set; }
    public string Status { get; set; } = string.Empty;
    public Dictionary<string, object> Details { get; set; } = new();
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Result of document analysis from Document Intelligence
/// </summary>
public class DocumentAnalysisResult
{
    public string Content { get; set; } = string.Empty;
    public DocumentPage[] Pages { get; set; } = Array.Empty<DocumentPage>();
    public DocumentTable[] Tables { get; set; } = Array.Empty<DocumentTable>();
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Represents a page in an analyzed document
/// </summary>
public class DocumentPage
{
    public int PageNumber { get; set; }
    public string Content { get; set; } = string.Empty;
    public float Width { get; set; }
    public float Height { get; set; }
}

/// <summary>
/// Represents a table in an analyzed document
/// </summary>
public class DocumentTable
{
    public int RowCount { get; set; }
    public int ColumnCount { get; set; }
    public DocumentTableCell[] Cells { get; set; } = Array.Empty<DocumentTableCell>();
}

/// <summary>
/// Represents a cell in a document table
/// </summary>
public class DocumentTableCell
{
    public int RowIndex { get; set; }
    public int ColumnIndex { get; set; }
    public string Content { get; set; } = string.Empty;
}