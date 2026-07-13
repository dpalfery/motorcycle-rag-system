using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Application.Services.Ingestion;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.UnitTests.Services.Ingestion;

public sealed class ChunkReprocessServiceTests
{
    private readonly Mock<IIndexedArtifactRepository> _artifacts = new();
    private readonly Mock<IIndexedChunkRepository> _chunks = new();
    private readonly Mock<IIngestionJobRepository> _jobs = new();
    private readonly Mock<IBlobStorageService> _blobStorage = new();
    private readonly Mock<IChunkIndexingService> _indexing = new();

    [Fact]
    public async Task ReprocessByJobIdAsync_WhenJobIsMissing_ReturnsAnEmptyResult()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        _jobs.Setup(repository => repository.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IngestionJob?)null);

        // Act
        var result = await CreateSut().ReprocessByJobIdAsync(jobId);

        // Assert
        result.Should().Be(new ReprocessResultDto(0, 0, 0, 0));
        _artifacts.Verify(repository => repository.GetByUploadAndTypeAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReprocessByJobIdAsync_WhenArtifactIsMissing_ReturnsAnEmptyResult()
    {
        // Arrange
        var job = CreateJob();
        _jobs.Setup(repository => repository.GetByIdAsync(job.IngestionJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _artifacts.Setup(repository => repository.GetByUploadAndTypeAsync(
                job.InputRef, "search-chunks", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IndexedArtifact?)null);

        // Act
        var result = await CreateSut().ReprocessByJobIdAsync(job.IngestionJobId);

        // Assert
        result.Should().Be(new ReprocessResultDto(0, 0, 0, 0));
        _blobStorage.Verify(service => service.ExistsAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReprocessByJobIdAsync_WhenArtifactBlobIsMissing_MarksArtifactAndJobAsFailed()
    {
        // Arrange
        var job = CreateJob();
        var artifact = CreateArtifact(job);
        ArrangeFoundJobAndArtifact(job, artifact);
        _blobStorage.Setup(service => service.ExistsAsync(
                artifact.BlobContainer, artifact.BlobPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        ArrangePersistence();

        // Act
        var result = await CreateSut().ReprocessByJobIdAsync(job.IngestionJobId);

        // Assert
        result.Should().Be(new ReprocessResultDto(1, 0, 0, 1));
        artifact.State.Should().Be(IndexedArtifactState.Failed);
        artifact.FailureReason.Should().Be("Chunk artifact blob not found.");
        _jobs.Verify(repository => repository.UpdateStatusAsync(
            job.IngestionJobId,
            IngestionJobStatus.Failed,
            "Chunk artifact blob not found.",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(3, 3, IndexedArtifactState.Completed, IngestionJobStatus.Completed, 1, 0, 0)]
    [InlineData(3, 1, IndexedArtifactState.PartiallyIndexed, IngestionJobStatus.PartiallyCompleted, 0, 1, 0)]
    [InlineData(2, 0, IndexedArtifactState.Failed, IngestionJobStatus.Failed, 0, 0, 1)]
    public async Task ReprocessByJobIdAsync_WhenIndexingCompletes_UpdatesArtifactChunksAndTerminalStatus(
        int totalParsed,
        int successfulChunks,
        IndexedArtifactState expectedArtifactState,
        IngestionJobStatus expectedJobStatus,
        int expectedSucceeded,
        int expectedPartial,
        int expectedFailed)
    {
        // Arrange
        var job = CreateJob();
        var artifact = CreateArtifact(job);
        ArrangeFoundJobAndArtifact(job, artifact);
        _blobStorage.Setup(service => service.ExistsAsync(
                artifact.BlobContainer, artifact.BlobPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _blobStorage.Setup(service => service.DownloadAsync(
                artifact.BlobContainer, artifact.BlobPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream([1, 2, 3]));
        _indexing.Setup(service => service.IndexFromJsonlAsync(
                It.IsAny<Stream>(), job.InputRef, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateIndexingResult(totalParsed, successfulChunks));
        ArrangePersistence();

        // Act
        var result = await CreateSut().ReprocessByJobIdAsync(job.IngestionJobId);

        // Assert
        result.Should().Be(new ReprocessResultDto(1, expectedSucceeded, expectedPartial, expectedFailed));
        artifact.State.Should().Be(expectedArtifactState);
        artifact.ExpectedChunkCount.Should().Be(totalParsed);
        artifact.IndexedChunkCount.Should().Be(successfulChunks);
        artifact.FailedChunkCount.Should().Be(totalParsed - successfulChunks);
        _chunks.Verify(repository => repository.DeleteByArtifactIdAsync(
            artifact.IndexedArtifactId, It.IsAny<CancellationToken>()), Times.Once);
        _chunks.Verify(repository => repository.UpsertManyAsync(
            It.Is<IReadOnlyCollection<IndexedChunk>>(items => items.Count == successfulChunks),
            It.IsAny<CancellationToken>()), successfulChunks > 0 ? Times.Once() : Times.Never());
        _jobs.Verify(repository => repository.UpdateStatusAsync(
            job.IngestionJobId,
            expectedJobStatus,
            expectedJobStatus == IngestionJobStatus.Failed ? "Chunk indexing failed." : null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReprocessByJobIdAsync_WhenDownloadOrIndexingFails_MarksArtifactAndJobAsFailed()
    {
        // Arrange
        var job = CreateJob();
        var artifact = CreateArtifact(job);
        ArrangeFoundJobAndArtifact(job, artifact);
        _blobStorage.Setup(service => service.ExistsAsync(
                artifact.BlobContainer, artifact.BlobPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _blobStorage.Setup(service => service.DownloadAsync(
                artifact.BlobContainer, artifact.BlobPath, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("blob unavailable"));
        ArrangePersistence();

        // Act
        var result = await CreateSut().ReprocessByJobIdAsync(job.IngestionJobId);

        // Assert
        result.Should().Be(new ReprocessResultDto(1, 0, 0, 1));
        artifact.State.Should().Be(IndexedArtifactState.Failed);
        artifact.FailureReason.Should().Contain("blob unavailable");
        _jobs.Verify(repository => repository.UpdateStatusAsync(
            job.IngestionJobId,
            IngestionJobStatus.Failed,
            "Failed to index chunks.",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private ChunkReprocessService CreateSut() =>
        new(
            _artifacts.Object,
            _chunks.Object,
            _jobs.Object,
            _blobStorage.Object,
            _indexing.Object,
            Options.Create(new BlobStorageOptions()),
            Options.Create(new IngestionOptions()),
            NullLogger<ChunkReprocessService>.Instance);

    private void ArrangeFoundJobAndArtifact(IngestionJob job, IndexedArtifact artifact)
    {
        _jobs.Setup(repository => repository.GetByIdAsync(job.IngestionJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _artifacts.Setup(repository => repository.GetByUploadAndTypeAsync(
                job.InputRef, "search-chunks", It.IsAny<CancellationToken>()))
            .ReturnsAsync(artifact);
    }

    private void ArrangePersistence()
    {
        _artifacts.Setup(repository => repository.UpsertAsync(
                It.IsAny<IndexedArtifact>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IndexedArtifact());
        _chunks.Setup(repository => repository.DeleteByArtifactIdAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _chunks.Setup(repository => repository.UpsertManyAsync(
                It.IsAny<IReadOnlyCollection<IndexedChunk>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _jobs.Setup(repository => repository.UpdateStatusAsync(
                It.IsAny<Guid>(), It.IsAny<IngestionJobStatus>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private static IngestionJob CreateJob() =>
        new()
        {
            IngestionJobId = Guid.NewGuid(),
            InputRef = "uploads/manual.jsonl",
        };

    private static IndexedArtifact CreateArtifact(IngestionJob job) =>
        new()
        {
            IndexedArtifactId = Guid.NewGuid(),
            IngestionJobId = job.IngestionJobId,
            UploadId = job.InputRef,
            ArtifactType = "search-chunks",
            BlobContainer = "processed",
            BlobPath = "chunks/manual.jsonl",
            State = IndexedArtifactState.Pending,
        };

    private static ChunkIndexingResult CreateIndexingResult(int totalParsed, int successfulChunks)
    {
        var outcomes = Enumerable.Range(0, totalParsed)
            .Select(index => new ChunkIndexOutcome(
                $"chunk-{index}",
                index < successfulChunks,
                index < successfulChunks ? null : "indexing failed",
                index + 1,
                index,
                "manual.pdf"))
            .ToArray();

        return new ChunkIndexingResult(totalParsed, 1, outcomes);
    }
}
