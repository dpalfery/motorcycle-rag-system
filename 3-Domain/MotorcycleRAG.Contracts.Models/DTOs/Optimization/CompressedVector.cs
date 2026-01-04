namespace MotorcycleRAG.Domain.DTOs.Optimization;

/// <summary>
/// Represents a compressed vector with metadata.
/// </summary>
public class CompressedVector {
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public int OriginalDimensions { get; set; }
    public CompressionMethod Method { get; set; }
    public int CompressionLevel { get; set; }
    public float MinValue { get; set; }
    public float MaxValue { get; set; }
    public DateTime CompressedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Compression methods available.
/// </summary>
public enum CompressionMethod {
    None = 0,
    Quantization8Bit = 1,
    Quantization4Bit = 2,
    ProductQuantization = 3,
    ScalarQuantization = 4
}
