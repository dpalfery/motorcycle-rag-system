namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Cache statistics for monitoring and optimization.
/// </summary>
public class CacheStatistics {
    public long TotalRequests { get; set; }
    public long CacheHits { get; set; }
    public long CacheMisses { get; set; }
    public double HitRatio => TotalRequests > 0 ? (double)CacheHits / TotalRequests : 0;
    public long TotalEntries { get; set; }
    public long TotalMemoryUsage { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
