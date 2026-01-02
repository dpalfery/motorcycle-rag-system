using MotorcycleRAG.Domain.DTOs.Optimization;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Interface for vector compression to reduce storage requirements and improve performance.
/// </summary>
public interface IVectorCompressionService
{
    /// <summary>
    /// Compresses a vector using quantization techniques.
    /// </summary>
    /// <param name="vector">The original vector</param>
    /// <param name="compressionLevel">Compression level (1-10, higher = more compression)</param>
    /// <returns>Compressed vector data</returns>
    CompressedVector CompressVector(float[] vector, int compressionLevel = 5);

    /// <summary>
    /// Decompresses a vector back to its original form (with some precision loss).
    /// </summary>
    /// <param name="compressedVector">The compressed vector data</param>
    /// <returns>Decompressed vector</returns>
    float[] DecompressVector(CompressedVector compressedVector);

    /// <summary>
    /// Compresses multiple vectors in batch for better efficiency.
    /// </summary>
    /// <param name="vectors">Array of vectors to compress</param>
    /// <param name="compressionLevel">Compression level</param>
    /// <returns>Array of compressed vectors</returns>
    CompressedVector[] CompressVectorsBatch(float[][] vectors, int compressionLevel = 5);

    /// <summary>
    /// Decompresses multiple vectors in batch.
    /// </summary>
    /// <param name="compressedVectors">Array of compressed vectors</param>
    /// <returns>Array of decompressed vectors</returns>
    float[][] DecompressVectorsBatch(CompressedVector[] compressedVectors);

    /// <summary>
    /// Calculates the compression ratio achieved.
    /// </summary>
    /// <param name="originalSize">Original size in bytes</param>
    /// <param name="compressedSize">Compressed size in bytes</param>
    /// <returns>Compression ratio (e.g., 0.5 = 50% compression)</returns>
    double CalculateCompressionRatio(int originalSize, int compressedSize);

    /// <summary>
    /// Gets compression statistics for monitoring.
    /// </summary>
    /// <returns>Compression statistics</returns>
    CompressionStatistics GetStatistics();
}
