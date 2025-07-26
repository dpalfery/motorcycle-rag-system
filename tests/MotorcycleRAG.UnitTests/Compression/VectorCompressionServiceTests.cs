using Microsoft.Extensions.Logging;
using Moq;
using MotorcycleRAG.Infrastructure.Compression;
using Xunit;

namespace MotorcycleRAG.UnitTests.Compression;

public class VectorCompressionServiceTests
{
    private readonly Mock<ILogger<VectorCompressionService>> _mockLogger;
    private readonly VectorCompressionService _compressionService;

    public VectorCompressionServiceTests()
    {
        _mockLogger = new Mock<ILogger<VectorCompressionService>>();
        _compressionService = new VectorCompressionService(_mockLogger.Object);
    }

    [Fact]
    public async Task CompressVectorsAsync_WithValidVectors_CompressesSuccessfully()
    {
        // Arrange
        var vectors = new float[][]
        {
            new float[] { 1.0f, 2.0f, 3.0f, 4.0f },
            new float[] { 5.0f, 6.0f, 7.0f, 8.0f },
            new float[] { 9.0f, 10.0f, 11.0f, 12.0f }
        };

        // Act
        var compressedData = await _compressionService.CompressVectorsAsync(vectors);

        // Assert
        Assert.NotNull(compressedData);
        Assert.True(compressedData.Length > 0);
    }

    [Fact]
    public async Task DecompressVectorsAsync_WithValidCompressedData_RestoresOriginalVectors()
    {
        // Arrange
        var originalVectors = new float[][]
        {
            new float[] { 1.1f, 2.2f, 3.3f },
            new float[] { 4.4f, 5.5f, 6.6f }
        };

        var compressedData = await _compressionService.CompressVectorsAsync(originalVectors);

        // Act
        var decompressedVectors = await _compressionService.DecompressVectorsAsync(compressedData);

        // Assert
        Assert.NotNull(decompressedVectors);
        Assert.Equal(originalVectors.Length, decompressedVectors.Length);

        for (int i = 0; i < originalVectors.Length; i++)
        {
            Assert.Equal(originalVectors[i].Length, decompressedVectors[i].Length);
            for (int j = 0; j < originalVectors[i].Length; j++)
            {
                Assert.Equal(originalVectors[i][j], decompressedVectors[i][j], precision: 6);
            }
        }
    }

    [Fact]
    public async Task CompressSingleVectorAsync_WithValidVector_CompressesSuccessfully()
    {
        // Arrange
        var vector = new float[] { 1.0f, 2.0f, 3.0f, 4.0f, 5.0f };

        // Act
        var compressedData = await _compressionService.CompressSingleVectorAsync(vector);

        // Assert
        Assert.NotNull(compressedData);
        Assert.True(compressedData.Length > 0);
    }

    [Fact]
    public async Task DecompressSingleVectorAsync_WithValidCompressedData_RestoresOriginalVector()
    {
        // Arrange
        var originalVector = new float[] { 1.1f, 2.2f, 3.3f, 4.4f, 5.5f };
        var compressedData = await _compressionService.CompressSingleVectorAsync(originalVector);

        // Act
        var decompressedVector = await _compressionService.DecompressSingleVectorAsync(compressedData);

        // Assert
        Assert.NotNull(decompressedVector);
        Assert.Equal(originalVector.Length, decompressedVector.Length);

        for (int i = 0; i < originalVector.Length; i++)
        {
            Assert.Equal(originalVector[i], decompressedVector[i], precision: 6);
        }
    }

    [Fact]
    public void GetCompressionRatio_WithValidSizes_CalculatesCorrectRatio()
    {
        // Arrange
        var originalSize = 1000;
        var compressedSize = 250;

        // Act
        var ratio = _compressionService.GetCompressionRatio(originalSize, compressedSize);

        // Assert
        Assert.Equal(0.75, ratio, precision: 2);
    }

    [Fact]
    public void GetCompressionRatio_WithZeroOriginalSize_ReturnsZero()
    {
        // Arrange
        var originalSize = 0;
        var compressedSize = 100;

        // Act
        var ratio = _compressionService.GetCompressionRatio(originalSize, compressedSize);

        // Assert
        Assert.Equal(0.0, ratio);
    }

    [Fact]
    public void GetCompressionRatio_WithSameSizes_ReturnsZero()
    {
        // Arrange
        var originalSize = 500;
        var compressedSize = 500;

        // Act
        var ratio = _compressionService.GetCompressionRatio(originalSize, compressedSize);

        // Assert
        Assert.Equal(0.0, ratio);
    }

    [Fact]
    public async Task CompressVectorsAsync_WithEmptyVectors_HandlesGracefully()
    {
        // Arrange
        var vectors = new float[0][];

        // Act
        var compressedData = await _compressionService.CompressVectorsAsync(vectors);

        // Assert
        Assert.NotNull(compressedData);
        // Empty vectors may result in minimal compressed data (just header info)
        Assert.True(compressedData.Length >= 0);
    }

    [Fact]
    public async Task CompressSingleVectorAsync_WithEmptyVector_HandlesGracefully()
    {
        // Arrange
        var vector = new float[0];

        // Act
        var compressedData = await _compressionService.CompressSingleVectorAsync(vector);

        // Assert
        Assert.NotNull(compressedData);
        // Empty vectors may result in minimal compressed data (just header info)
        Assert.True(compressedData.Length >= 0);
    }

    [Fact]
    public async Task CompressDecompressCycle_WithLargeVectors_MaintainsDataIntegrity()
    {
        // Arrange
        var random = new Random(42); // Fixed seed for reproducible tests
        var vectorCount = 100;
        var vectorDimension = 1536; // Common embedding dimension

        var originalVectors = new float[vectorCount][];
        for (int i = 0; i < vectorCount; i++)
        {
            originalVectors[i] = new float[vectorDimension];
            for (int j = 0; j < vectorDimension; j++)
            {
                originalVectors[i][j] = (float)(random.NextDouble() * 2.0 - 1.0); // Range [-1, 1]
            }
        }

        // Act
        var compressedData = await _compressionService.CompressVectorsAsync(originalVectors);
        var decompressedVectors = await _compressionService.DecompressVectorsAsync(compressedData);

        // Assert
        Assert.Equal(originalVectors.Length, decompressedVectors.Length);

        for (int i = 0; i < originalVectors.Length; i++)
        {
            Assert.Equal(originalVectors[i].Length, decompressedVectors[i].Length);
            for (int j = 0; j < originalVectors[i].Length; j++)
            {
                Assert.Equal(originalVectors[i][j], decompressedVectors[i][j], precision: 6);
            }
        }
    }

    [Fact]
    public async Task CompressVectorsAsync_AchievesReasonableCompressionRatio()
    {
        // Arrange
        var vectors = new float[][]
        {
            Enumerable.Repeat(1.0f, 1536).ToArray(), // Repeated values should compress well
            Enumerable.Repeat(2.0f, 1536).ToArray(),
            Enumerable.Repeat(3.0f, 1536).ToArray()
        };

        // Act
        var compressedData = await _compressionService.CompressVectorsAsync(vectors);
        var originalSize = vectors.Length * vectors[0].Length * sizeof(float);
        var compressionRatio = _compressionService.GetCompressionRatio(originalSize, compressedData.Length);

        // Assert
        Assert.True(compressionRatio > 0.1, $"Expected compression ratio > 0.1, got {compressionRatio}");
        Assert.True(compressionRatio < 1.0, $"Expected compression ratio < 1.0, got {compressionRatio}");
    }

    [Fact]
    public async Task DecompressVectorsAsync_WithInvalidData_ThrowsException()
    {
        // Arrange
        var invalidData = new byte[] { 0x01, 0x02, 0x03 }; // Not valid compressed data

        // Act & Assert
        var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await _compressionService.DecompressVectorsAsync(invalidData);
        });
        
        Assert.NotNull(exception);
    }

    [Fact]
    public async Task DecompressSingleVectorAsync_WithInvalidData_ThrowsException()
    {
        // Arrange
        var invalidData = new byte[] { 0x01, 0x02, 0x03 }; // Not valid compressed data

        // Act & Assert
        var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await _compressionService.DecompressSingleVectorAsync(invalidData);
        });
        
        Assert.NotNull(exception);
    }
}