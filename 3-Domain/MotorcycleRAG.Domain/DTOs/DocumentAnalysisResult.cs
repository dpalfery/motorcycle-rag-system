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
/// Represents a page in an analyzed document with locator metadata
/// </summary>
public class DocumentPage
{
    public int PageNumber { get; set; }
    public string Content { get; set; } = string.Empty;
    public float Width { get; set; }
    public float Height { get; set; }
    
    /// <summary>
    /// Primary section heading for this page (best-effort extraction)
    /// </summary>
    public string PrimarySection { get; set; } = string.Empty;
    
    /// <summary>
    /// All detected section headings on this page in order
    /// </summary>
    public string[] SectionHeadings { get; set; } = Array.Empty<string>();
    
    /// <summary>
    /// Hierarchy level of primary section (1=chapter, 2=section, 3=subsection)
    /// </summary>
    public int SectionLevel { get; set; }
}

/// <summary>
/// Represents a table in an analyzed document with page span metadata
/// </summary>
public class DocumentTable
{
    public int RowCount { get; set; }
    public int ColumnCount { get; set; }
    public DocumentTableCell[] Cells { get; set; } = Array.Empty<DocumentTableCell>();
    
    /// <summary>
    /// Starting page number for this table
    /// </summary>
    public int StartPageNumber { get; set; }
    
    /// <summary>
    /// Ending page number for this table (may span multiple pages)
    /// </summary>
    public int EndPageNumber { get; set; }
    
    /// <summary>
    /// Caption or title of the table if detected
    /// </summary>
    public string Caption { get; set; } = string.Empty;
    
    /// <summary>
    /// Section heading where table is located
    /// </summary>
    public string Section { get; set; } = string.Empty;
}

/// <summary>
/// Represents a cell in a document table with position metadata
/// </summary>
public class DocumentTableCell
{
    public int RowIndex { get; set; }
    public int ColumnIndex { get; set; }
    public string Content { get; set; } = string.Empty;
    
    /// <summary>
    /// Page number where this cell appears (for multi-page tables)
    /// </summary>
    public int PageNumber { get; set; }
    
    /// <summary>
    /// Whether this cell is a header row
    /// </summary>
    public bool IsHeader { get; set; }
}
