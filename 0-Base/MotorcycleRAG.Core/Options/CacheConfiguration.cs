namespace MotorcycleRAG.Core.Options;

/// <summary>
/// Configuration for caching behavior.
/// </summary>
public class CacheConfiguration {
    public bool EnableCaching { get; set; } = true;
    public TimeSpan DefaultExpiration { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan LongTermExpiration { get; set; } = TimeSpan.FromHours(24);
    public int MaxMemorySizeMB { get; set; } = 100;
    public bool EnableCompression { get; set; } = true;
    public int CompressionThreshold { get; set; } = 1024;
    public int MaxCacheEntries { get; set; } = 1000;
}
