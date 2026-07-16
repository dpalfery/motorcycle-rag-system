using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MotorcycleRAG.Application.Services.Ingestion;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.UnitTests.Services.Ingestion;

public sealed class ManualPageQueryServiceTests
{
    private readonly Mock<IManualPageAssetStore> _assetStore = new();

    private ManualPageQueryService CreateSut() =>
        new(_assetStore.Object, NullLogger<ManualPageQueryService>.Instance);

    [Fact]
    public void Constructor_NullAssetStore_ThrowsArgumentNullException()
    {
        var act = () => new ManualPageQueryService(null!, NullLogger<ManualPageQueryService>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("assetStore");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new ManualPageQueryService(_assetStore.Object, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetPageAsync_PageNumberLessThanOne_ThrowsArgumentOutOfRangeException(int pageNumber)
    {
        var sut = CreateSut();
        var manualId = Guid.NewGuid();

        var act = () => sut.GetPageAsync(manualId, pageNumber, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithParameterName("pageNumber");
        _assetStore.Verify(s => s.PageExistsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetPageAsync_PageNotFound_ThrowsKeyNotFoundException()
    {
        var manualId = Guid.NewGuid();
        _assetStore.Setup(s => s.PageExistsAsync(manualId, 1, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var sut = CreateSut();

        var act = () => sut.GetPageAsync(manualId, 1, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*Page not found for the requested manual.*");
    }

    [Fact]
    public async Task GetPageAsync_PageExists_ReturnsStreamAndEtag()
    {
        var manualId = Guid.NewGuid();
        var stream = new MemoryStream([1, 2, 3]);
        const string etag = "\"abc123\"";
        _assetStore.Setup(s => s.PageExistsAsync(manualId, 5, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _assetStore.Setup(s => s.DownloadPageAsync(manualId, 5, It.IsAny<CancellationToken>())).ReturnsAsync(stream);
        _assetStore.Setup(s => s.GetETagAsync(manualId, 5, It.IsAny<CancellationToken>())).ReturnsAsync(etag);

        var sut = CreateSut();

        var result = await sut.GetPageAsync(manualId, 5, CancellationToken.None);

        result.Should().NotBeNull();
        result.Content.Should().BeSameAs(stream);
        result.ETag.Should().Be(etag);
    }

    [Fact]
    public async Task GetPageAsync_EtagIsNull_ReturnsStreamAndNullEtag()
    {
        var manualId = Guid.NewGuid();
        var stream = new MemoryStream();
        _assetStore.Setup(s => s.PageExistsAsync(manualId, 1, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _assetStore.Setup(s => s.DownloadPageAsync(manualId, 1, It.IsAny<CancellationToken>())).ReturnsAsync(stream);
        _assetStore.Setup(s => s.GetETagAsync(manualId, 1, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        var sut = CreateSut();

        var result = await sut.GetPageAsync(manualId, 1, CancellationToken.None);

        result.ETag.Should().BeNull();
    }
}
