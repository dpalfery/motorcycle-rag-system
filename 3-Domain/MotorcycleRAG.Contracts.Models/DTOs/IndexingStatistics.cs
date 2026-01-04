using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MotorcycleRAG.Domain.DTOs;

/// <summary>
/// Statistics about indexing operations and index health
/// </summary>
public class IndexingStatistics {
    public Collection<IndexInfo> Indexes { get; } = new();
    public long TotalDocuments { get; set; }
    public long TotalStorageSize { get; set; }
    public int HealthyIndexes { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public DateTime RetrievedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Information about a specific search index
/// </summary>
public class IndexInfo {
    public string Name { get; set; } = string.Empty;
    public long DocumentCount { get; set; }
    public long StorageSize { get; set; }
    public bool IsHealthy { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
