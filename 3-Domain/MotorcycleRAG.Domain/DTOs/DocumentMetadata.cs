using System;
using System.Collections.Generic;
using System.Text;

namespace MotorcycleRAG.Domain.DTOs
{
    /// <summary>
    /// Document metadata for additional context
    /// </summary>
    public class DocumentMetadata
    {
        public string SourceFile { get; set; } = string.Empty;
        public string SourceUrl { get; set; } = string.Empty;
        public int PageNumber { get; set; }
        public string Section { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public DateTime PublishedDate { get; set; }
        public List<string> Tags { get; set; } = new();
        public Dictionary<string, object> AdditionalProperties { get; set; } = new();
    }
}
