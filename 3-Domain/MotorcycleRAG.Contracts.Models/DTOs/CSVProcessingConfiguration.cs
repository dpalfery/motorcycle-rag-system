using System;
using System.Collections.ObjectModel;

namespace MotorcycleRAG.Contracts.Models.DTOs {
    public class CSVProcessingConfiguration {
        public char Delimiter { get; set; } = ',';
        public bool HasHeader { get; set; } = true;
        // Backward-compatible aliases expected by processors
        public bool PreserveRelationalIntegrity { get; set; } = true;
        public int ChunkSize { get; set; } = 50;
        public int MaxRows { get; set; } = 0;
        public Collection<string> IdentifierFields { get; } = new();
        public Collection<string> EmbeddingFields { get; } = new();
    }
}
