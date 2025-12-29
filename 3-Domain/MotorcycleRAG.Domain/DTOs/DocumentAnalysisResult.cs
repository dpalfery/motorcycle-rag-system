using System;
using System.Collections.Generic;

namespace MotorcycleRAG.Domain.DTOs;

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
