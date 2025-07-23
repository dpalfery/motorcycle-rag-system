namespace MotorcycleRAG.Core.Models;

/// <summary>
/// Result of index creation operations
/// </summary>
public class IndexCreationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> CreatedIndexes { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Statistics about indexing operations and index health
/// </summary>
public class IndexingStatistics
{
    public List<IndexInfo> Indexes { get; set; } = new();
    public long TotalDocuments { get; set; }
    public long TotalStorageSize { get; set; }
    public int HealthyIndexes { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public DateTime RetrievedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Information about a specific search index
/// </summary>
public class IndexInfo
{
    public string Name { get; set; } = string.Empty;
    public long DocumentCount { get; set; }
    public long StorageSize { get; set; }
    public bool IsHealthy { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}



/// <summary>
/// Batch indexing operation result
/// </summary>
public class BatchIndexingResult
{
    public bool Success { get; set; }
    public int DocumentsProcessed { get; set; }
    public int DocumentsIndexed { get; set; }
    public List<string> Errors { get; set; } = new();
    public TimeSpan ProcessingTime { get; set; }
    public string BatchId { get; set; } = Guid.NewGuid().ToString();
}

/// <summary>
/// Field mapping configuration for index creation
/// </summary>
public class FieldMappingConfiguration
{
    public Dictionary<string, string> FieldMappings { get; set; } = new();
    public List<string> SearchableFields { get; set; } = new();
    public List<string> FilterableFields { get; set; } = new();
    public List<string> FacetableFields { get; set; } = new();
    public List<string> SortableFields { get; set; } = new();
}

/// <summary>
/// Metadata management configuration
/// </summary>
public class MetadataConfiguration
{
    public bool IncludeSourceMetadata { get; set; } = true;
    public bool IncludeProcessingMetadata { get; set; } = true;
    public List<string> RequiredMetadataFields { get; set; } = new();
    public Dictionary<string, object> DefaultMetadataValues { get; set; } = new();
}