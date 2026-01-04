namespace MotorcycleRAG.Domain.DTOs.Optimization;

/// <summary>
/// Statistics for vector compression operations.
/// </summary>
public class CompressionStatistics {
    public long TotalVectorsCompressed { get; set; }
    public long TotalVectorsDecompressed { get; set; }
    public long TotalBytesOriginal { get; set; }
    public long TotalBytesCompressed { get; set; }
    public double AverageCompressionRatio => TotalBytesOriginal > 0 ? (double)TotalBytesCompressed / TotalBytesOriginal : 0;
    public TimeSpan TotalCompressionTime { get; set; }
    public TimeSpan TotalDecompressionTime { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
