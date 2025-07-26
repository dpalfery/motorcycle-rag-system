namespace MotorcycleRAG.Core.Interfaces;

/// <summary>
/// Interface for vector compression and storage optimization
/// </summary>
public interface IVectorCompressionService
{
    /// <summary>
    /// Compress vector embeddings for storage efficiency
    /// </summary>
    /// <param name="vectors">Array of float vectors to compress</param>
    /// <returns>Compressed vector data</returns>
    Task<byte[]> CompressVectorsAsync(float[][] vectors);

    /// <summary>
    /// Decompress vector embeddings for use
    /// </summary>
    /// <param name="compressedData">Compressed vector data</param>
    /// <returns>Array of decompressed float vectors</returns>
    Task<float[][]> DecompressVectorsAsync(byte[] compressedData);

    /// <summary>
    /// Compress a single vector embedding
    /// </summary>
    /// <param name="vector">Float vector to compress</param>
    /// <returns>Compressed vector data</returns>
    Task<byte[]> CompressSingleVectorAsync(float[] vector);

    /// <summary>
    /// Decompress a single vector embedding
    /// </summary>
    /// <param name="compressedData">Compressed vector data</param>
    /// <returns>Decompressed float vector</returns>
    Task<float[]> DecompressSingleVectorAsync(byte[] compressedData);

    /// <summary>
    /// Get compression ratio achieved
    /// </summary>
    /// <param name="originalSize">Original data size in bytes</param>
    /// <param name="compressedSize">Compressed data size in bytes</param>
    /// <returns>Compression ratio (0.0 to 1.0)</returns>
    double GetCompressionRatio(int originalSize, int compressedSize);
}