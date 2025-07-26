using Microsoft.Extensions.Logging;
using MotorcycleRAG.Core.Interfaces;
using System.IO.Compression;
using System.Text.Json;

namespace MotorcycleRAG.Infrastructure.Compression;

/// <summary>
/// Implementation of vector compression service for storage efficiency
/// </summary>
public class VectorCompressionService : IVectorCompressionService
{
    private readonly ILogger<VectorCompressionService> _logger;

    public VectorCompressionService(ILogger<VectorCompressionService> logger)
    {
        _logger = logger;
    }

    public async Task<byte[]> CompressVectorsAsync(float[][] vectors)
    {
        try
        {
            _logger.LogDebug("Compressing {Count} vectors", vectors.Length);

            // Serialize vectors to JSON
            var json = JsonSerializer.Serialize(vectors);
            var jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);

            // Compress using GZip
            using var compressedStream = new MemoryStream();
            using (var gzipStream = new GZipStream(compressedStream, CompressionLevel.Optimal))
            {
                await gzipStream.WriteAsync(jsonBytes);
            }

            var compressedData = compressedStream.ToArray();
            
            var compressionRatio = GetCompressionRatio(jsonBytes.Length, compressedData.Length);
            _logger.LogDebug("Vector compression completed. Original: {Original} bytes, Compressed: {Compressed} bytes, Ratio: {Ratio:P2}", 
                jsonBytes.Length, compressedData.Length, compressionRatio);

            return compressedData;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error compressing vectors");
            throw;
        }
    }

    public async Task<float[][]> DecompressVectorsAsync(byte[] compressedData)
    {
        try
        {
            _logger.LogDebug("Decompressing vector data of {Size} bytes", compressedData.Length);

            // Decompress using GZip
            using var compressedStream = new MemoryStream(compressedData);
            using var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress);
            using var decompressedStream = new MemoryStream();
            
            await gzipStream.CopyToAsync(decompressedStream);
            var jsonBytes = decompressedStream.ToArray();

            // Deserialize from JSON
            var json = System.Text.Encoding.UTF8.GetString(jsonBytes);
            var vectors = JsonSerializer.Deserialize<float[][]>(json);

            if (vectors == null)
            {
                throw new InvalidOperationException("Failed to deserialize vectors from compressed data");
            }

            _logger.LogDebug("Vector decompression completed. Restored {Count} vectors", vectors.Length);
            return vectors;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error decompressing vectors");
            throw;
        }
    }

    public async Task<byte[]> CompressSingleVectorAsync(float[] vector)
    {
        try
        {
            _logger.LogDebug("Compressing single vector of dimension {Dimension}", vector.Length);

            // For single vectors, use a more efficient approach
            var bytes = new byte[vector.Length * sizeof(float)];
            Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);

            // Compress using GZip
            using var compressedStream = new MemoryStream();
            using (var gzipStream = new GZipStream(compressedStream, CompressionLevel.Optimal))
            {
                await gzipStream.WriteAsync(bytes);
            }

            var compressedData = compressedStream.ToArray();
            
            var compressionRatio = GetCompressionRatio(bytes.Length, compressedData.Length);
            _logger.LogDebug("Single vector compression completed. Original: {Original} bytes, Compressed: {Compressed} bytes, Ratio: {Ratio:P2}", 
                bytes.Length, compressedData.Length, compressionRatio);

            return compressedData;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error compressing single vector");
            throw;
        }
    }

    public async Task<float[]> DecompressSingleVectorAsync(byte[] compressedData)
    {
        try
        {
            _logger.LogDebug("Decompressing single vector from {Size} bytes", compressedData.Length);

            // Decompress using GZip
            using var compressedStream = new MemoryStream(compressedData);
            using var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress);
            using var decompressedStream = new MemoryStream();
            
            await gzipStream.CopyToAsync(decompressedStream);
            var bytes = decompressedStream.ToArray();

            // Convert bytes back to float array
            var vector = new float[bytes.Length / sizeof(float)];
            Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);

            _logger.LogDebug("Single vector decompression completed. Restored vector of dimension {Dimension}", vector.Length);
            return vector;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error decompressing single vector");
            throw;
        }
    }

    public double GetCompressionRatio(int originalSize, int compressedSize)
    {
        if (originalSize <= 0) return 0.0;
        return 1.0 - ((double)compressedSize / originalSize);
    }
}