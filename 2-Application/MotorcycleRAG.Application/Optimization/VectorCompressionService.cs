using Microsoft.Extensions.Logging;
using System.Diagnostics;
using MotorcycleRAG.Contracts.Optimization;


namespace MotorcycleRAG.Application.Optimization;

/// <summary>
/// Implementation of vector compression using quantization techniques to reduce storage requirements.
/// </summary>
public class VectorCompressionService : IVectorCompressionService
{
    private readonly ILogger<VectorCompressionService> _logger;
    private readonly object _statsLock = new();
    private CompressionStatistics _statistics = new();

    public VectorCompressionService(ILogger<VectorCompressionService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public CompressedVector CompressVector(float[] vector, int compressionLevel = 5)
    {
        if (vector == null || vector.Length == 0)
            throw new ArgumentException("Vector cannot be null or empty", nameof(vector));

        if (compressionLevel < 1 || compressionLevel > 10)
            throw new ArgumentOutOfRangeException(nameof(compressionLevel), "Compression level must be between 1 and 10");

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var method = DetermineCompressionMethod(compressionLevel);
            var compressedData = method switch
            {
                CompressionMethod.Quantization8Bit => Compress8Bit(vector),
                CompressionMethod.Quantization4Bit => Compress4Bit(vector),
                CompressionMethod.ScalarQuantization => CompressScalarQuantization(vector, compressionLevel),
                _ => CompressDefault(vector)
            };

            var result = new CompressedVector
            {
                Data = compressedData.Data,
                OriginalDimensions = vector.Length,
                Method = method,
                CompressionLevel = compressionLevel,
                MinValue = compressedData.MinValue,
                MaxValue = compressedData.MaxValue
            };

            stopwatch.Stop();

            // Update statistics
            lock (_statsLock)
            {
                _statistics.TotalVectorsCompressed++;
                _statistics.TotalBytesOriginal += vector.Length * sizeof(float);
                _statistics.TotalBytesCompressed += result.Data.Length;
                _statistics.TotalCompressionTime += stopwatch.Elapsed;
                _statistics.LastUpdated = DateTime.UtcNow;
            }

            _logger.LogDebug("Compressed vector: {Dimensions} dimensions, {OriginalSize} -> {CompressedSize} bytes, Method: {Method}",
                vector.Length, vector.Length * sizeof(float), result.Data.Length, method);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error compressing vector with {Dimensions} dimensions", vector.Length);
            throw;
        }
    }

    public float[] DecompressVector(CompressedVector compressedVector)
    {
        if (compressedVector == null)
            throw new ArgumentNullException(nameof(compressedVector));

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = compressedVector.Method switch
            {
                CompressionMethod.Quantization8Bit => Decompress8Bit(compressedVector),
                CompressionMethod.Quantization4Bit => Decompress4Bit(compressedVector),
                CompressionMethod.ScalarQuantization => DecompressScalarQuantization(compressedVector),
                _ => DecompressDefault(compressedVector)
            };

            stopwatch.Stop();

            // Update statistics
            lock (_statsLock)
            {
                _statistics.TotalVectorsDecompressed++;
                _statistics.TotalDecompressionTime += stopwatch.Elapsed;
                _statistics.LastUpdated = DateTime.UtcNow;
            }

            _logger.LogDebug("Decompressed vector: {Dimensions} dimensions, Method: {Method}",
                result.Length, compressedVector.Method);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error decompressing vector with method {Method}", compressedVector.Method);
            throw;
        }
    }

    public CompressedVector[] CompressVectorsBatch(float[][] vectors, int compressionLevel = 5)
    {
        if (vectors == null || vectors.Length == 0)
            return Array.Empty<CompressedVector>();

        _logger.LogDebug("Compressing batch of {Count} vectors", vectors.Length);

        var results = new CompressedVector[vectors.Length];
        
        // Use parallel processing for large batches
        if (vectors.Length > 100)
        {
            Parallel.For(0, vectors.Length, i =>
            {
                results[i] = CompressVector(vectors[i], compressionLevel);
            });
        }
        else
        {
            for (int i = 0; i < vectors.Length; i++)
            {
                results[i] = CompressVector(vectors[i], compressionLevel);
            }
        }

        return results;
    }

    public float[][] DecompressVectorsBatch(CompressedVector[] compressedVectors)
    {
        if (compressedVectors == null || compressedVectors.Length == 0)
            return Array.Empty<float[]>();

        _logger.LogDebug("Decompressing batch of {Count} vectors", compressedVectors.Length);

        var results = new float[compressedVectors.Length][];
        
        // Use parallel processing for large batches
        if (compressedVectors.Length > 100)
        {
            Parallel.For(0, compressedVectors.Length, i =>
            {
                results[i] = DecompressVector(compressedVectors[i]);
            });
        }
        else
        {
            for (int i = 0; i < compressedVectors.Length; i++)
            {
                results[i] = DecompressVector(compressedVectors[i]);
            }
        }

        return results;
    }

    public double CalculateCompressionRatio(int originalSize, int compressedSize)
    {
        if (originalSize <= 0) return 0;
        return (double)compressedSize / originalSize;
    }

    public CompressionStatistics GetStatistics()
    {
        lock (_statsLock)
        {
            return new CompressionStatistics
            {
                TotalVectorsCompressed = _statistics.TotalVectorsCompressed,
                TotalVectorsDecompressed = _statistics.TotalVectorsDecompressed,
                TotalBytesOriginal = _statistics.TotalBytesOriginal,
                TotalBytesCompressed = _statistics.TotalBytesCompressed,
                TotalCompressionTime = _statistics.TotalCompressionTime,
                TotalDecompressionTime = _statistics.TotalDecompressionTime,
                LastUpdated = _statistics.LastUpdated
            };
        }
    }

    private CompressionMethod DetermineCompressionMethod(int compressionLevel)
    {
        return compressionLevel switch
        {
            <= 3 => CompressionMethod.Quantization8Bit,
            <= 6 => CompressionMethod.ScalarQuantization,
            <= 8 => CompressionMethod.Quantization4Bit,
            _ => CompressionMethod.Quantization4Bit
        };
    }

    private (byte[] Data, float MinValue, float MaxValue) Compress8Bit(float[] vector)
    {
        var minValue = vector.Min();
        var maxValue = vector.Max();
        var range = maxValue - minValue;
        
        if (range == 0)
        {
            // All values are the same
            return (new byte[vector.Length], minValue, maxValue);
        }

        var quantized = new byte[vector.Length];
        for (int i = 0; i < vector.Length; i++)
        {
            var normalized = (vector[i] - minValue) / range;
            quantized[i] = (byte)(normalized * 255);
        }

        return (quantized, minValue, maxValue);
    }

    private float[] Decompress8Bit(CompressedVector compressed)
    {
        var range = compressed.MaxValue - compressed.MinValue;
        var result = new float[compressed.OriginalDimensions];
        
        if (range == 0)
        {
            // All values were the same
            Array.Fill(result, compressed.MinValue);
            return result;
        }

        for (int i = 0; i < compressed.Data.Length; i++)
        {
            var normalized = compressed.Data[i] / 255.0f;
            result[i] = compressed.MinValue + normalized * range;
        }

        return result;
    }

    private (byte[] Data, float MinValue, float MaxValue) Compress4Bit(float[] vector)
    {
        var minValue = vector.Min();
        var maxValue = vector.Max();
        var range = maxValue - minValue;
        
        if (range == 0)
        {
            // All values are the same
            var sameValueData = new byte[(vector.Length + 1) / 2];
            return (sameValueData, minValue, maxValue);
        }

        var quantized = new byte[(vector.Length + 1) / 2];
        for (int i = 0; i < vector.Length; i += 2)
        {
            var normalized1 = (vector[i] - minValue) / range;
            var quantized1 = (byte)(normalized1 * 15);
            
            byte quantized2 = 0;
            if (i + 1 < vector.Length)
            {
                var normalized2 = (vector[i + 1] - minValue) / range;
                quantized2 = (byte)(normalized2 * 15);
            }
            
            quantized[i / 2] = (byte)((quantized1 << 4) | quantized2);
        }

        return (quantized, minValue, maxValue);
    }

    private float[] Decompress4Bit(CompressedVector compressed)
    {
        var range = compressed.MaxValue - compressed.MinValue;
        var result = new float[compressed.OriginalDimensions];
        
        if (range == 0)
        {
            // All values were the same
            Array.Fill(result, compressed.MinValue);
            return result;
        }

        for (int i = 0; i < compressed.Data.Length; i++)
        {
            var packedByte = compressed.Data[i];
            var quantized1 = (packedByte >> 4) & 0x0F;
            var quantized2 = packedByte & 0x0F;
            
            var normalized1 = quantized1 / 15.0f;
            result[i * 2] = compressed.MinValue + normalized1 * range;
            
            if (i * 2 + 1 < compressed.OriginalDimensions)
            {
                var normalized2 = quantized2 / 15.0f;
                result[i * 2 + 1] = compressed.MinValue + normalized2 * range;
            }
        }

        return result;
    }

    private (byte[] Data, float MinValue, float MaxValue) CompressScalarQuantization(float[] vector, int compressionLevel)
    {
        // Adaptive scalar quantization based on compression level
        var bitsPerValue = Math.Max(4, 12 - compressionLevel);
        var maxQuantizedValue = (1 << bitsPerValue) - 1;
        
        var minValue = vector.Min();
        var maxValue = vector.Max();
        var range = maxValue - minValue;
        
        if (range == 0)
        {
            var sameValueData = new byte[(vector.Length * bitsPerValue + 7) / 8];
            return (sameValueData, minValue, maxValue);
        }

        var quantizedValues = new int[vector.Length];
        for (int i = 0; i < vector.Length; i++)
        {
            var normalized = (vector[i] - minValue) / range;
            quantizedValues[i] = (int)(normalized * maxQuantizedValue);
        }

        // Pack quantized values into bytes
        var packedData = PackQuantizedValues(quantizedValues, bitsPerValue);
        
        return (packedData, minValue, maxValue);
    }

    private float[] DecompressScalarQuantization(CompressedVector compressed)
    {
        // Determine bits per value from compression level
        var bitsPerValue = Math.Max(4, 12 - compressed.CompressionLevel);
        var maxQuantizedValue = (1 << bitsPerValue) - 1;
        
        var range = compressed.MaxValue - compressed.MinValue;
        
        if (range == 0)
        {
            var result = new float[compressed.OriginalDimensions];
            Array.Fill(result, compressed.MinValue);
            return result;
        }

        // Unpack quantized values from bytes
        var quantizedValues = UnpackQuantizedValues(compressed.Data, compressed.OriginalDimensions, bitsPerValue);
        
        var decompressed = new float[compressed.OriginalDimensions];
        for (int i = 0; i < decompressed.Length; i++)
        {
            var normalized = quantizedValues[i] / (float)maxQuantizedValue;
            decompressed[i] = compressed.MinValue + normalized * range;
        }

        return decompressed;
    }

    private (byte[] Data, float MinValue, float MaxValue) CompressDefault(float[] vector)
    {
        // Default to 8-bit quantization
        return Compress8Bit(vector);
    }

    private float[] DecompressDefault(CompressedVector compressed)
    {
        // Default to 8-bit decompression
        return Decompress8Bit(compressed);
    }

    private byte[] PackQuantizedValues(int[] values, int bitsPerValue)
    {
        var totalBits = values.Length * bitsPerValue;
        var totalBytes = (totalBits + 7) / 8;
        var packed = new byte[totalBytes];
        
        int bitOffset = 0;
        foreach (var value in values)
        {
            for (int bit = 0; bit < bitsPerValue; bit++)
            {
                var byteIndex = bitOffset / 8;
                var bitIndex = bitOffset % 8;
                
                if ((value & (1 << bit)) != 0)
                {
                    packed[byteIndex] |= (byte)(1 << bitIndex);
                }
                
                bitOffset++;
            }
        }
        
        return packed;
    }

    private int[] UnpackQuantizedValues(byte[] packed, int valueCount, int bitsPerValue)
    {
        var values = new int[valueCount];
        
        int bitOffset = 0;
        for (int i = 0; i < valueCount; i++)
        {
            int value = 0;
            for (int bit = 0; bit < bitsPerValue; bit++)
            {
                var byteIndex = bitOffset / 8;
                var bitIndex = bitOffset % 8;
                
                if (byteIndex < packed.Length && (packed[byteIndex] & (1 << bitIndex)) != 0)
                {
                    value |= (1 << bit);
                }
                
                bitOffset++;
            }
            values[i] = value;
        }
        
        return values;
    }
}