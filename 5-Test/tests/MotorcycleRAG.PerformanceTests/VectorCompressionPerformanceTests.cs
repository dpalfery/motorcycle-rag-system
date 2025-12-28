using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Application.Optimization;
using MotorcycleRAG.Contracts.Optimization;

namespace MotorcycleRAG.PerformanceTests;

/// <summary>
/// Performance benchmarks for vector compression functionality.
/// </summary>
[MemoryDiagnoser]
[SimpleJob]
public class VectorCompressionPerformanceTests
{
    private IVectorCompressionService _compressionService = null!;
    private float[] _testVector1536 = null!;
    private float[] _testVector3072 = null!;
    private float[][] _testVectorBatch = null!;
    private CompressedVector _compressedVector = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IVectorCompressionService, VectorCompressionService>();

        var serviceProvider = services.BuildServiceProvider();
        _compressionService = serviceProvider.GetRequiredService<IVectorCompressionService>();

        // Create test vectors with realistic data
        _testVector1536 = GenerateRealisticVector(1536);
        _testVector3072 = GenerateRealisticVector(3072);
        
        // Create batch of vectors
        _testVectorBatch = new float[100][];
        for (int i = 0; i < 100; i++)
        {
            _testVectorBatch[i] = GenerateRealisticVector(1536);
        }

        // Pre-compress a vector for decompression tests
        _compressedVector = _compressionService.CompressVector(_testVector1536, 5);
    }

    private float[] GenerateRealisticVector(int dimensions)
    {
        var random = new Random(42); // Fixed seed for consistent benchmarks
        var vector = new float[dimensions];
        
        // Generate realistic embedding-like values (mostly small values with some larger ones)
        for (int i = 0; i < dimensions; i++)
        {
            vector[i] = (float)(random.NextGaussian() * 0.1); // Normal distribution around 0
        }
        
        return vector;
    }

    [Benchmark]
    public CompressedVector CompressVector1536_Level5()
    {
        return _compressionService.CompressVector(_testVector1536, 5);
    }

    [Benchmark]
    public CompressedVector CompressVector3072_Level5()
    {
        return _compressionService.CompressVector(_testVector3072, 5);
    }

    [Benchmark]
    [Arguments(1)]
    [Arguments(3)]
    [Arguments(5)]
    [Arguments(8)]
    [Arguments(10)]
    public CompressedVector CompressVectorDifferentLevels(int compressionLevel)
    {
        return _compressionService.CompressVector(_testVector1536, compressionLevel);
    }

    [Benchmark]
    public float[] DecompressVector()
    {
        return _compressionService.DecompressVector(_compressedVector);
    }

    [Benchmark]
    public CompressedVector[] CompressBatch()
    {
        return _compressionService.CompressVectorsBatch(_testVectorBatch, 5);
    }

    [Benchmark]
    public float[][] DecompressBatch()
    {
        var compressed = _compressionService.CompressVectorsBatch(_testVectorBatch, 5);
        return _compressionService.DecompressVectorsBatch(compressed);
    }
}

/// <summary>
/// Unit tests for vector compression performance validation.
/// </summary>
public class VectorCompressionPerformanceValidationTests
{
    private readonly IVectorCompressionService _compressionService;

    public VectorCompressionPerformanceValidationTests()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IVectorCompressionService, VectorCompressionService>();

        var serviceProvider = services.BuildServiceProvider();
        _compressionService = serviceProvider.GetRequiredService<IVectorCompressionService>();
    }

    [Fact]
    public void VectorCompression_ShouldMeetPerformanceTargets()
    {
        // Arrange
        var vector = GenerateTestVector(1536);

        // Act & Assert - Compression Performance
        var compressionStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var compressed = _compressionService.CompressVector(vector, 5);
        compressionStopwatch.Stop();

        compressionStopwatch.ElapsedMilliseconds.Should().BeLessThan(100, 
            "Vector compression should complete within 100ms");

        // Act & Assert - Decompression Performance
        var decompressionStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var decompressed = _compressionService.DecompressVector(compressed);
        decompressionStopwatch.Stop();

        decompressionStopwatch.ElapsedMilliseconds.Should().BeLessThan(50, 
            "Vector decompression should complete within 50ms");

        // Validate compression ratio
        var originalSize = vector.Length * sizeof(float);
        var compressedSize = compressed.Data.Length;
        var compressionRatio = _compressionService.CalculateCompressionRatio(originalSize, compressedSize);

        compressionRatio.Should().BeLessThan(0.8, "Compression should achieve at least 20% size reduction");
        decompressed.Should().HaveCount(vector.Length);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(10)]
    public void VectorCompression_DifferentLevels_ShouldProduceValidResults(int compressionLevel)
    {
        // Arrange
        var vector = GenerateTestVector(1536);

        // Act
        var compressed = _compressionService.CompressVector(vector, compressionLevel);
        var decompressed = _compressionService.DecompressVector(compressed);

        // Assert
        compressed.CompressionLevel.Should().Be(compressionLevel);
        decompressed.Should().HaveCount(vector.Length);
        
        // Higher compression levels should produce smaller results
        var originalSize = vector.Length * sizeof(float);
        var compressionRatio = _compressionService.CalculateCompressionRatio(originalSize, compressed.Data.Length);
        compressionRatio.Should().BeLessThan(1.0, "Compression should reduce size");
    }

    [Fact]
    public void BatchVectorCompression_ShouldBeEfficient()
    {
        // Arrange
        var vectors = new float[100][];
        for (int i = 0; i < 100; i++)
        {
            vectors[i] = GenerateTestVector(1536);
        }

        // Act - Batch Compression
        var batchStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var compressedBatch = _compressionService.CompressVectorsBatch(vectors, 5);
        batchStopwatch.Stop();

        // Act - Individual Compression for comparison
        var individualStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var individualCompressed = new CompressedVector[100];
        for (int i = 0; i < 100; i++)
        {
            individualCompressed[i] = _compressionService.CompressVector(vectors[i], 5);
        }
        individualStopwatch.Stop();

        // Assert
        compressedBatch.Should().HaveCount(100);
        
        // Batch processing should be more efficient for large batches
        if (vectors.Length >= 100)
        {
            batchStopwatch.ElapsedMilliseconds.Should().BeLessThanOrEqualTo(
                individualStopwatch.ElapsedMilliseconds * 1.2, // Allow 20% overhead
                "Batch compression should be competitive with individual compression");
        }
    }

    [Fact]
    public void VectorCompressionStatistics_ShouldTrackOperations()
    {
        // Arrange
        var vector = GenerateTestVector(1536);

        // Act
        var compressed = _compressionService.CompressVector(vector, 5);
        var decompressed = _compressionService.DecompressVector(compressed);
        var stats = _compressionService.GetStatistics();

        // Assert
        stats.TotalVectorsCompressed.Should().BeGreaterThan(0);
        stats.TotalVectorsDecompressed.Should().BeGreaterThan(0);
        stats.TotalBytesOriginal.Should().BeGreaterThan(0);
        stats.TotalBytesCompressed.Should().BeGreaterThan(0);
        stats.AverageCompressionRatio.Should().BeGreaterThan(0).And.BeLessThan(1);
        stats.TotalCompressionTime.Should().BeGreaterThan(TimeSpan.Zero);
        stats.TotalDecompressionTime.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void VectorCompressionAccuracy_ShouldMaintainReasonablePrecision()
    {
        // Arrange
        var vector = GenerateTestVector(1536);

        // Act
        var compressed = _compressionService.CompressVector(vector, 5);
        var decompressed = _compressionService.DecompressVector(compressed);

        // Assert - Calculate mean squared error
        var mse = 0.0;
        for (int i = 0; i < vector.Length; i++)
        {
            var error = vector[i] - decompressed[i];
            mse += error * error;
        }
        mse /= vector.Length;

        // MSE should be reasonable for the compression level
        mse.Should().BeLessThan(0.01, "Mean squared error should be acceptable for compression level 5");
        
        // Cosine similarity should be high
        var cosineSimilarity = CalculateCosineSimilarity(vector, decompressed);
        cosineSimilarity.Should().BeGreaterThan(0.95, "Cosine similarity should remain high after compression");
    }

    private float[] GenerateTestVector(int dimensions)
    {
        var random = new Random(42); // Fixed seed for consistent tests
        var vector = new float[dimensions];
        
        for (int i = 0; i < dimensions; i++)
        {
            vector[i] = (float)(random.NextGaussian() * 0.1);
        }
        
        return vector;
    }

    private double CalculateCosineSimilarity(float[] vector1, float[] vector2)
    {
        if (vector1.Length != vector2.Length)
            return 0;

        double dot = 0, mag1 = 0, mag2 = 0;
        for (int i = 0; i < vector1.Length; i++)
        {
            dot += vector1[i] * vector2[i];
            mag1 += vector1[i] * vector1[i];
            mag2 += vector2[i] * vector2[i];
        }

        return dot / (Math.Sqrt(mag1) * Math.Sqrt(mag2) + 1e-8);
    }
}

/// <summary>
/// Extension methods for generating random numbers with normal distribution.
/// </summary>
public static class RandomExtensions
{
    public static double NextGaussian(this Random random, double mean = 0, double stdDev = 1)
    {
        // Box-Muller transform
        static double NextGaussianInternal(Random r)
        {
            double u1 = 1.0 - r.NextDouble();
            double u2 = 1.0 - r.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        }

        return mean + stdDev * NextGaussianInternal(random);
    }
}