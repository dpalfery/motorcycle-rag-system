using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using MotorcycleRAG.Contracts.Models.Serialization;
using MotorcycleRAG.Domain.ValueObjects;

namespace MotorcycleRAG.Contracts.Models.DTOs {
    public class CSVFile {
        public string FileName { get; set; } = string.Empty;
        public Stream? Content { get; set; }
        public long Size { get; set; }
        public Dictionary<string, string>? Metadata { get; set; }
        // Added to match expected consumer properties in processors
        public bool HasHeaders { get; set; } = true;
        public string Delimiter { get; set; } = ",";
        public string Encoding { get; set; } = "UTF-8";
        public int MaxColumns { get; set; } = 150;
        public string Source { get; set; } = string.Empty;
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Motorcycle category (Dirt, Touring, Sport, Cruiser). Selects the target
        /// category-partitioned Azure AI Search index for this file's chunks.
        /// Nullable so existing callers that omit it continue to compile.
        /// </summary>
        [JsonConverter(typeof(MotorcycleCategoryJsonConverter))]
        public MotorcycleCategory? Category { get; set; }
    }
}
