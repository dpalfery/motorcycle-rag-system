using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Application.Services.Ingestion;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using Xunit;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.UnitTests.Services.Ingestion;

public class ChunkReprocessServiceTests
{
    private readonly Mock<IIndexedArtifactRepository> _artifactRepoMock;
    private readonly Mock<IIndexedChunkRepository> _chunkRepoMock;
    private readonly Mock<IIngestionJobRepository> _jobRepoMock;
    private readonly Mock<IBlobStorageService> _blobStorageMock;
    private readonly Mock<IChunkIndexingService> _indexingMock;
    private readonly ChunkReprocessService _sut;

    public ChunkReprocessServiceTests()
    {
        _artifactRepoMock = new Mock<IIndexedArtifactRepository>();
        _chunkRepoMock = new Mock<IIndexedChunkRepository>();
        _jobRepoMock = new Mock<IIngestionJobRepository>();
        _blobStorageMock = new Mock<IBlobStorageService>();
        _indexingMock = new Mock<IChunkIndexingService>();
        
        var blobOptions = Options.Create(new BlobStorageOptions());
        var ingestionOptions = Options.Create(new IngestionOptions());

        _sut = new ChunkReprocessService(
            _artifactRepoMock.Object,
            _chunkRepoMock.Object,
            _jobRepoMock.Object,
            _blobStorageMock.Object,
            _indexingMock.Object,
            blobOptions,
            ingestionOptions,
            NullLogger<ChunkReprocessService>.Instance);
    }

    [Fact]
    public async Task ReprocessAllAsync_ReturnsCorrectDto()
    {
        _artifactRepoMock.Setup(x => x.GetAllAsync(10000, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<IndexedArtifact>());

        var result = await _sut.ReprocessAllAsync();

        result.ArtifactsProcessed.Should().Be(0);
    }

    [Fact]
    public async Task ReprocessAllNotSucceededAsync_ReturnsCorrectDto()
    {
        _artifactRepoMock.Setup(x => x.GetByStatesAsync(It.IsAny<IndexedArtifactState[]>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<IndexedArtifact>());

        var result = await _sut.ReprocessAllNotSucceededAsync();

        result.ArtifactsProcessed.Should().Be(0);
    }
}
