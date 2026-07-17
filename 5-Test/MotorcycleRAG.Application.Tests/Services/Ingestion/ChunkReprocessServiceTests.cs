using System;
using System.Collections.Generic;
using System.IO;
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
using MotorcycleRAG.Contracts.Models.DTOs.Ingestion;

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
        _artifactRepoMock.Setup(x => x.GetAllAsync(10000, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<IndexedArtifactDto>());

        var result = await _sut.ReprocessAllAsync();

        result.ArtifactsProcessed.Should().Be(0);
    }

    [Fact]
    public async Task ReprocessAllNotSucceededAsync_ReturnsCorrectDto()
    {
        _artifactRepoMock.Setup(x => x.GetByStatesAsync(It.IsAny<IndexedArtifactState[]>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<IndexedArtifactDto>());

        var result = await _sut.ReprocessAllNotSucceededAsync();

        result.ArtifactsProcessed.Should().Be(0);
    }

    [Fact]
    public async Task ReprocessAllAsync_WhenOneArtifactSucceedsAndAnotherThrows_ContinuesAndAggregatesSuccessfulWork()
    {
        var successfulJob = CreateJob("uploads/success");
        var failingJobId = Guid.NewGuid();
        var successfulArtifact = CreateArtifact(successfulJob.IngestionJobId, successfulJob.InputRef);
        var failingArtifact = CreateArtifact(failingJobId, "uploads/failure");
        using var cts = new CancellationTokenSource();
        _artifactRepoMock
            .Setup(x => x.GetAllAsync(10000, cts.Token))
            .ReturnsAsync([successfulArtifact, failingArtifact]);
        _jobRepoMock
            .Setup(x => x.GetByIdAsync(successfulJob.IngestionJobId, cts.Token))
            .ReturnsAsync(successfulJob);
        _jobRepoMock
            .Setup(x => x.GetByIdAsync(failingJobId, cts.Token))
            .ThrowsAsync(new InvalidOperationException("repository unavailable"));
        _artifactRepoMock
            .Setup(x => x.GetByUploadAndTypeAsync(successfulJob.InputRef, "search-chunks", cts.Token))
            .ReturnsAsync(successfulArtifact);
        _blobStorageMock
            .Setup(x => x.ExistsAsync(successfulArtifact.BlobContainer, successfulArtifact.BlobPath, cts.Token))
            .ReturnsAsync(true);
        _blobStorageMock
            .Setup(x => x.DownloadAsync(successfulArtifact.BlobContainer, successfulArtifact.BlobPath, cts.Token))
            .ReturnsAsync(() => new MemoryStream());
        _indexingMock
            .Setup(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), successfulJob.InputRef, cts.Token))
            .ReturnsAsync(new ChunkIndexingResult(1, 1, [new ChunkIndexOutcome("chunk-1", true, null, 1, 0, "manual.pdf")]));

        var result = await _sut.ReprocessAllAsync(cts.Token);

        result.Should().Be(new ReprocessResultDto(1, 1, 0, 0));
        _jobRepoMock.Verify(x => x.GetByIdAsync(failingJobId, cts.Token), Times.Once);
        _jobRepoMock.Verify(x => x.UpdateStatusAsync(successfulJob.IngestionJobId, IngestionJobStatus.Completed, null, cts.Token), Times.Once);
    }

    [Fact]
    public async Task ReprocessAllNotSucceededAsync_WhenArtifactsHaveMixedTerminalOutcomes_AggregatesPartialAndFailedCounts()
    {
        var partialJob = CreateJob("uploads/partial");
        var failedJob = CreateJob("uploads/failed");
        var partialArtifact = CreateArtifact(partialJob.IngestionJobId, partialJob.InputRef);
        var failedArtifact = CreateArtifact(failedJob.IngestionJobId, failedJob.InputRef);
        _artifactRepoMock
            .Setup(x => x.GetByStatesAsync(It.IsAny<IndexedArtifactState[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([partialArtifact, failedArtifact]);
        _jobRepoMock.Setup(x => x.GetByIdAsync(partialJob.IngestionJobId, It.IsAny<CancellationToken>())).ReturnsAsync(partialJob);
        _jobRepoMock.Setup(x => x.GetByIdAsync(failedJob.IngestionJobId, It.IsAny<CancellationToken>())).ReturnsAsync(failedJob);
        _artifactRepoMock.Setup(x => x.GetByUploadAndTypeAsync(partialJob.InputRef, "search-chunks", It.IsAny<CancellationToken>())).ReturnsAsync(partialArtifact);
        _artifactRepoMock.Setup(x => x.GetByUploadAndTypeAsync(failedJob.InputRef, "search-chunks", It.IsAny<CancellationToken>())).ReturnsAsync(failedArtifact);
        _blobStorageMock.Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _blobStorageMock.Setup(x => x.DownloadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => new MemoryStream());
        _indexingMock.Setup(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), partialJob.InputRef, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChunkIndexingResult(2, 1, [new ChunkIndexOutcome("one", true, null, 1, 0, "manual.pdf"), new ChunkIndexOutcome("two", false, "failed", 1, 1, "manual.pdf")]));
        _indexingMock.Setup(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), failedJob.InputRef, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChunkIndexingResult(1, 1, [new ChunkIndexOutcome("three", false, "failed", 1, 0, "manual.pdf")]));

        var result = await _sut.ReprocessAllNotSucceededAsync();

        result.Should().Be(new ReprocessResultDto(2, 0, 1, 1));
        _jobRepoMock.Verify(x => x.UpdateStatusAsync(partialJob.IngestionJobId, IngestionJobStatus.PartiallyCompleted, null, It.IsAny<CancellationToken>()), Times.Once);
        _jobRepoMock.Verify(x => x.UpdateStatusAsync(failedJob.IngestionJobId, IngestionJobStatus.Failed, "Chunk indexing failed.", It.IsAny<CancellationToken>()), Times.Once);
    }

    private static IngestionJob CreateJob(string inputRef = "uploads/abc") =>
        IngestionJob.Create(IngestionJobType.PDFManual, inputRef, createdBySubject: null);

    private static IndexedArtifactDto CreateArtifact(Guid jobId, string uploadId = "uploads/abc") =>
        new()
        {
            IndexedArtifactId = Guid.NewGuid(),
            IngestionJobId = jobId,
            UploadId = uploadId,
            ArtifactType = "search-chunks",
            BlobContainer = "chunks",
            BlobPath = "path/to/chunks.jsonl",
            State = IndexedArtifactState.Pending,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

    [Fact]
    public async Task ReprocessByJobIdAsync_JobNotFound_ReturnsEmptyResultAndDoesNotTouchArtifacts()
    {
        var jobId = Guid.NewGuid();
        _jobRepoMock.Setup(x => x.GetByIdAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync((IngestionJob?)null);

        var result = await _sut.ReprocessByJobIdAsync(jobId);

        result.Should().Be(new ReprocessResultDto(0, 0, 0, 0));
        _artifactRepoMock.Verify(x => x.GetByUploadAndTypeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReprocessByJobIdAsync_ArtifactNotFound_ReturnsEmptyResultAndDoesNotTouchBlobStorage()
    {
        var job = CreateJob();
        var jobId = job.IngestionJobId;
        _jobRepoMock.Setup(x => x.GetByIdAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        _artifactRepoMock
            .Setup(x => x.GetByUploadAndTypeAsync(job.InputRef, "search-chunks", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IndexedArtifactDto?)null);

        var result = await _sut.ReprocessByJobIdAsync(jobId);

        result.Should().Be(new ReprocessResultDto(0, 0, 0, 0));
        _blobStorageMock.Verify(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReprocessByJobIdAsync_BlobMissing_MarksArtifactAndJobFailed()
    {
        var job = CreateJob();
        var jobId = job.IngestionJobId;
        var artifact = CreateArtifact(jobId, job.InputRef);
        _jobRepoMock.Setup(x => x.GetByIdAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        _artifactRepoMock
            .Setup(x => x.GetByUploadAndTypeAsync(job.InputRef, "search-chunks", It.IsAny<CancellationToken>()))
            .ReturnsAsync(artifact);
        _blobStorageMock
            .Setup(x => x.ExistsAsync(artifact.BlobContainer, artifact.BlobPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _sut.ReprocessByJobIdAsync(jobId);

        result.Should().Be(new ReprocessResultDto(1, 0, 0, 1));
        artifact.State.Should().Be(IndexedArtifactState.Failed);
        artifact.FailureReason.Should().Be("Chunk artifact blob not found.");
        _artifactRepoMock.Verify(x => x.UpsertAsync(artifact, It.IsAny<CancellationToken>()), Times.Once);
        _jobRepoMock.Verify(x => x.UpdateStatusAsync(jobId, IngestionJobStatus.Failed, "Chunk artifact blob not found.", It.IsAny<CancellationToken>()), Times.Once);
        _blobStorageMock.Verify(x => x.DownloadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReprocessByJobIdAsync_DownloadThrows_MarksArtifactAndJobFailed()
    {
        var job = CreateJob();
        var jobId = job.IngestionJobId;
        var artifact = CreateArtifact(jobId, job.InputRef);
        _jobRepoMock.Setup(x => x.GetByIdAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        _artifactRepoMock
            .Setup(x => x.GetByUploadAndTypeAsync(job.InputRef, "search-chunks", It.IsAny<CancellationToken>()))
            .ReturnsAsync(artifact);
        _blobStorageMock
            .Setup(x => x.ExistsAsync(artifact.BlobContainer, artifact.BlobPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _blobStorageMock
            .Setup(x => x.DownloadAsync(artifact.BlobContainer, artifact.BlobPath, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("blob storage unavailable"));

        var result = await _sut.ReprocessByJobIdAsync(jobId);

        result.Should().Be(new ReprocessResultDto(1, 0, 0, 1));
        artifact.State.Should().Be(IndexedArtifactState.Failed);
        artifact.FailureReason.Should().Contain("blob storage unavailable");
        _artifactRepoMock.Verify(x => x.UpsertAsync(artifact, It.IsAny<CancellationToken>()), Times.Once);
        _jobRepoMock.Verify(x => x.UpdateStatusAsync(jobId, IngestionJobStatus.Failed, "Failed to index chunks.", It.IsAny<CancellationToken>()), Times.Once);
        _chunkRepoMock.Verify(x => x.DeleteByArtifactIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReprocessByJobIdAsync_IndexingThrows_MarksArtifactAndJobFailed()
    {
        var job = CreateJob();
        var jobId = job.IngestionJobId;
        var artifact = CreateArtifact(jobId, job.InputRef);
        _jobRepoMock.Setup(x => x.GetByIdAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        _artifactRepoMock
            .Setup(x => x.GetByUploadAndTypeAsync(job.InputRef, "search-chunks", It.IsAny<CancellationToken>()))
            .ReturnsAsync(artifact);
        _blobStorageMock
            .Setup(x => x.ExistsAsync(artifact.BlobContainer, artifact.BlobPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _blobStorageMock
            .Setup(x => x.DownloadAsync(artifact.BlobContainer, artifact.BlobPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream());
        _indexingMock
            .Setup(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), job.InputRef, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("search index unavailable"));

        var result = await _sut.ReprocessByJobIdAsync(jobId);

        result.Should().Be(new ReprocessResultDto(1, 0, 0, 1));
        artifact.State.Should().Be(IndexedArtifactState.Failed);
        artifact.FailureReason.Should().Contain("search index unavailable");
        _jobRepoMock.Verify(x => x.UpdateStatusAsync(jobId, IngestionJobStatus.Failed, "Failed to index chunks.", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReprocessByJobIdAsync_AllChunksIndexed_MarksCompletedAndUpsertsChunks()
    {
        var job = CreateJob();
        var jobId = job.IngestionJobId;
        var artifact = CreateArtifact(jobId, job.InputRef);
        _jobRepoMock.Setup(x => x.GetByIdAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        _artifactRepoMock
            .Setup(x => x.GetByUploadAndTypeAsync(job.InputRef, "search-chunks", It.IsAny<CancellationToken>()))
            .ReturnsAsync(artifact);
        _blobStorageMock
            .Setup(x => x.ExistsAsync(artifact.BlobContainer, artifact.BlobPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _blobStorageMock
            .Setup(x => x.DownloadAsync(artifact.BlobContainer, artifact.BlobPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream());

        var outcomes = new List<ChunkIndexOutcome>
        {
            new("chunk-1", true, null, 1, 0, "file.pdf"),
            new("chunk-2", true, null, 1, 1, "file.pdf")
        };
        var indexingResult = new ChunkIndexingResult(2, 1, outcomes);
        _indexingMock
            .Setup(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), job.InputRef, It.IsAny<CancellationToken>()))
            .ReturnsAsync(indexingResult);

        var result = await _sut.ReprocessByJobIdAsync(jobId);

        result.Should().Be(new ReprocessResultDto(1, 1, 0, 0));
        artifact.State.Should().Be(IndexedArtifactState.Completed);
        artifact.ExpectedChunkCount.Should().Be(2);
        artifact.IndexedChunkCount.Should().Be(2);
        artifact.FailedChunkCount.Should().Be(0);
        artifact.FailureReason.Should().BeNull();
        _chunkRepoMock.Verify(x => x.DeleteByArtifactIdAsync(artifact.IndexedArtifactId, It.IsAny<CancellationToken>()), Times.Once);
        _chunkRepoMock.Verify(
            x => x.UpsertManyAsync(
                It.Is<IReadOnlyCollection<IndexedChunkDto>>(chunks => chunks.Count == 2),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _jobRepoMock.Verify(x => x.UpdateStatusAsync(jobId, IngestionJobStatus.Completed, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReprocessByJobIdAsync_SomeChunksFailed_MarksPartiallyIndexed()
    {
        var job = CreateJob();
        var jobId = job.IngestionJobId;
        var artifact = CreateArtifact(jobId, job.InputRef);
        _jobRepoMock.Setup(x => x.GetByIdAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        _artifactRepoMock
            .Setup(x => x.GetByUploadAndTypeAsync(job.InputRef, "search-chunks", It.IsAny<CancellationToken>()))
            .ReturnsAsync(artifact);
        _blobStorageMock
            .Setup(x => x.ExistsAsync(artifact.BlobContainer, artifact.BlobPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _blobStorageMock
            .Setup(x => x.DownloadAsync(artifact.BlobContainer, artifact.BlobPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream());

        var outcomes = new List<ChunkIndexOutcome>
        {
            new("chunk-1", true, null, 1, 0, "file.pdf"),
            new("chunk-2", false, "embedding failure", 1, 1, "file.pdf")
        };
        var indexingResult = new ChunkIndexingResult(2, 1, outcomes);
        _indexingMock
            .Setup(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), job.InputRef, It.IsAny<CancellationToken>()))
            .ReturnsAsync(indexingResult);

        var result = await _sut.ReprocessByJobIdAsync(jobId);

        result.Should().Be(new ReprocessResultDto(1, 0, 1, 0));
        artifact.State.Should().Be(IndexedArtifactState.PartiallyIndexed);
        artifact.IndexedChunkCount.Should().Be(1);
        artifact.FailedChunkCount.Should().Be(1);
        artifact.FailureReason.Should().BeNull();
        _chunkRepoMock.Verify(
            x => x.UpsertManyAsync(
                It.Is<IReadOnlyCollection<IndexedChunkDto>>(chunks => chunks.Count == 1),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _jobRepoMock.Verify(x => x.UpdateStatusAsync(jobId, IngestionJobStatus.PartiallyCompleted, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReprocessByJobIdAsync_NoChunksIndexed_MarksArtifactAndJobFailedAndSkipsUpsertMany()
    {
        var job = CreateJob();
        var jobId = job.IngestionJobId;
        var artifact = CreateArtifact(jobId, job.InputRef);
        _jobRepoMock.Setup(x => x.GetByIdAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        _artifactRepoMock
            .Setup(x => x.GetByUploadAndTypeAsync(job.InputRef, "search-chunks", It.IsAny<CancellationToken>()))
            .ReturnsAsync(artifact);
        _blobStorageMock
            .Setup(x => x.ExistsAsync(artifact.BlobContainer, artifact.BlobPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _blobStorageMock
            .Setup(x => x.DownloadAsync(artifact.BlobContainer, artifact.BlobPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream());

        var outcomes = new List<ChunkIndexOutcome>
        {
            new("chunk-1", false, "embedding failure", 1, 0, "file.pdf")
        };
        var indexingResult = new ChunkIndexingResult(1, 1, outcomes);
        _indexingMock
            .Setup(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), job.InputRef, It.IsAny<CancellationToken>()))
            .ReturnsAsync(indexingResult);

        var result = await _sut.ReprocessByJobIdAsync(jobId);

        result.Should().Be(new ReprocessResultDto(1, 0, 0, 1));
        artifact.State.Should().Be(IndexedArtifactState.Failed);
        artifact.FailureReason.Should().Be("No chunks were successfully indexed.");
        _chunkRepoMock.Verify(x => x.DeleteByArtifactIdAsync(artifact.IndexedArtifactId, It.IsAny<CancellationToken>()), Times.Once);
        _chunkRepoMock.Verify(x => x.UpsertManyAsync(It.IsAny<IReadOnlyCollection<IndexedChunkDto>>(), It.IsAny<CancellationToken>()), Times.Never);
        _jobRepoMock.Verify(x => x.UpdateStatusAsync(jobId, IngestionJobStatus.Failed, "Chunk indexing failed.", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReprocessByJobIdAsync_ZeroExpectedChunks_MarksFailed()
    {
        var job = CreateJob();
        var jobId = job.IngestionJobId;
        var artifact = CreateArtifact(jobId, job.InputRef);
        _jobRepoMock.Setup(x => x.GetByIdAsync(jobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        _artifactRepoMock
            .Setup(x => x.GetByUploadAndTypeAsync(job.InputRef, "search-chunks", It.IsAny<CancellationToken>()))
            .ReturnsAsync(artifact);
        _blobStorageMock
            .Setup(x => x.ExistsAsync(artifact.BlobContainer, artifact.BlobPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _blobStorageMock
            .Setup(x => x.DownloadAsync(artifact.BlobContainer, artifact.BlobPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream());

        var indexingResult = new ChunkIndexingResult(0, 0, new List<ChunkIndexOutcome>());
        _indexingMock
            .Setup(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), job.InputRef, It.IsAny<CancellationToken>()))
            .ReturnsAsync(indexingResult);

        var result = await _sut.ReprocessByJobIdAsync(jobId);

        result.Should().Be(new ReprocessResultDto(1, 0, 0, 1));
        artifact.State.Should().Be(IndexedArtifactState.Failed);
        _jobRepoMock.Verify(x => x.UpdateStatusAsync(jobId, IngestionJobStatus.Failed, "Chunk indexing failed.", It.IsAny<CancellationToken>()), Times.Once);
    }
}
