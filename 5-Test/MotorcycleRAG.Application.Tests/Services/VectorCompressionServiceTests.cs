using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.Application.Services.Optimization;
using MotorcycleRAG.Contracts.Models.DTOs.Optimization;

namespace MotorcycleRAG.UnitTests.Services;

public sealed class VectorCompressionServiceTests
{
    private static VectorCompressionService CreateSut() =>
        new(NullLogger<VectorCompressionService>.Instance);

    [Theory]
    [InlineData(1, CompressionMethod.Quantization8Bit, 0.02f)]
    [InlineData(5, CompressionMethod.ScalarQuantization, 0.02f)]
    [InlineData(7, CompressionMethod.Quantization4Bit, 0.15f)]
    [InlineData(9, CompressionMethod.Quantization4Bit, 0.15f)]
    public void CompressVector_WithSupportedLevels_RoundTripsWithinQuantizationTolerance(
        int compressionLevel,
        CompressionMethod expectedMethod,
        float tolerance)
    {
        // Arrange
        var sut = CreateSut();
        var vector = new[] { -1f, -0.25f, 0.5f, 1f };

        // Act
        var compressed = sut.CompressVector(vector, compressionLevel);
        var decompressed = sut.DecompressVector(compressed);

        // Assert
        compressed.Method.Should().Be(expectedMethod);
        compressed.OriginalDimensions.Should().Be(vector.Length);
        compressed.Data.Should().NotBeEmpty();
        decompressed.Should().HaveCount(vector.Length);
        for (var index = 0; index < vector.Length; index++)
        {
            decompressed[index].Should().BeApproximately(vector[index], tolerance);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(7)]
    public void CompressVector_WithConstantValues_RestoresTheOriginalValue(int compressionLevel)
    {
        // Arrange
        var sut = CreateSut();
        var vector = new[] { 3.5f, 3.5f, 3.5f };

        // Act
        var compressed = sut.CompressVector(vector, compressionLevel);
        var decompressed = sut.DecompressVector(compressed);

        // Assert
        decompressed.Should().Equal(vector);
    }

    [Fact]
    public void CompressVector_WithInvalidInput_ThrowsAnActionableException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var nullVector = () => sut.CompressVector(null!);
        var emptyVector = () => sut.CompressVector([]);
        var invalidLevel = () => sut.CompressVector([1f], 11);

        // Assert
        nullVector.Should().Throw<ArgumentNullException>();
        emptyVector.Should().Throw<ArgumentException>().WithMessage("*empty*");
        invalidLevel.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void DecompressVector_WithDefaultAndInvalidPayloads_UsesFallbackOrWrapsTheFailure()
    {
        // Arrange
        var sut = CreateSut();
        var fallback = new CompressedVector
        {
            Data = [0, byte.MaxValue],
            OriginalDimensions = 2,
            Method = CompressionMethod.None,
            CompressionLevel = 1,
            MinValue = -2f,
            MaxValue = 2f,
        };
        var malformed = new CompressedVector
        {
            OriginalDimensions = -1,
            Method = CompressionMethod.Quantization8Bit,
        };

        // Act
        var decompressed = sut.DecompressVector(fallback);
        var act = () => sut.DecompressVector(malformed);

        // Assert
        decompressed.Should().Equal(-2f, 2f);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Error decompressing vector*");
    }

    [Fact]
    public void BatchOperations_WithSmallAndLargeCollections_ProcessEveryVectorAndUpdateStatistics()
    {
        // Arrange
        var sut = CreateSut();
        var smallBatch = new[] { new[] { 0f, 1f }, new[] { 2f, 3f } };
        var largeBatch = Enumerable.Range(0, 101)
            .Select(index => new[] { (float)index, index + 1f })
            .ToArray();

        // Act
        var smallCompressed = sut.CompressVectorsBatch(smallBatch, 1);
        var smallDecompressed = sut.DecompressVectorsBatch(smallCompressed);
        var largeCompressed = sut.CompressVectorsBatch(largeBatch, 5);
        var largeDecompressed = sut.DecompressVectorsBatch(largeCompressed);
        var statistics = sut.GetStatistics();

        // Assert
        smallDecompressed.Should().BeEquivalentTo(smallBatch);
        largeDecompressed.Should().HaveCount(largeBatch.Length);
        statistics.TotalVectorsCompressed.Should().Be(smallBatch.Length + largeBatch.Length);
        statistics.TotalVectorsDecompressed.Should().Be(smallBatch.Length + largeBatch.Length);
        statistics.TotalBytesOriginal.Should().BeGreaterThan(0);
        statistics.TotalBytesCompressed.Should().BeGreaterThan(0);
        statistics.AverageCompressionRatio.Should().BeGreaterThan(0);
    }

    [Fact]
    public void BatchOperations_WithNullOrEmptyCollections_ReturnEmptyResultsAndCompressionRatioHandlesZero()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var compressedNull = sut.CompressVectorsBatch(null!);
        var compressedEmpty = sut.CompressVectorsBatch([]);
        var decompressedNull = sut.DecompressVectorsBatch(null!);
        var decompressedEmpty = sut.DecompressVectorsBatch([]);

        // Assert
        compressedNull.Should().BeEmpty();
        compressedEmpty.Should().BeEmpty();
        decompressedNull.Should().BeEmpty();
        decompressedEmpty.Should().BeEmpty();
        sut.CalculateCompressionRatio(0, 10).Should().Be(0);
        sut.CalculateCompressionRatio(16, 4).Should().Be(0.25);
    }
}
