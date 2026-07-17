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
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;
using Xunit;
using MotorcycleRAG.Contracts.Models.DTOs.Ingestion;

namespace MotorcycleRAG.UnitTests.Services.Ingestion;

public class ProcessorArtifactServiceTests
{
    private readonly Mock<IBlobStorageService> _blobStorageMock;
    private readonly Mock<IChunkIndexingService> _chunkIndexingMock;
    private readonly Mock<IIngestionJobRepository> _jobRepoMock;
    private readonly Mock<IIndexedArtifactRepository> _artifactRepoMock;
    private readonly Mock<IIndexedChunkRepository> _chunkRepoMock;
    private readonly Mock<IIngestionSourceAccessTokenService> _tokenServiceMock;
    private readonly Mock<IIngestionJobService> _ingestionJobServiceMock;
    private readonly ProcessorArtifactService _sut;

    public ProcessorArtifactServiceTests()
    {
        _blobStorageMock = new Mock<IBlobStorageService>();
        _chunkIndexingMock = new Mock<IChunkIndexingService>();
        _jobRepoMock = new Mock<IIngestionJobRepository>();
        _artifactRepoMock = new Mock<IIndexedArtifactRepository>();
        _chunkRepoMock = new Mock<IIndexedChunkRepository>();
        _tokenServiceMock = new Mock<IIngestionSourceAccessTokenService>();
        _ingestionJobServiceMock = new Mock<IIngestionJobService>();

        var blobOptions = Options.Create(new BlobStorageOptions());

        _sut = new ProcessorArtifactService(
            _blobStorageMock.Object,
            blobOptions,
            _chunkIndexingMock.Object,
            _jobRepoMock.Object,
            _artifactRepoMock.Object,
            _chunkRepoMock.Object,
            _tokenServiceMock.Object,
            _ingestionJobServiceMock.Object,
            NullLogger<ProcessorArtifactService>.Instance);
    }

    /// <summary>A MemoryStream override that reports itself as non-seekable, for exercising the seekability guard.</summary>
    private sealed class NonSeekableStream : MemoryStream
    {
        public override bool CanSeek => false;
    }

    private void SetUpNoJobFound(string uploadId)
    {
        foreach (var inputType in new[] { IngestionJobType.PDFManual, IngestionJobType.StructuredSpecification, IngestionJobType.Batch })
        {
            _jobRepoMock
                .Setup(x => x.GetLatestByInputAsync(uploadId, inputType, It.IsAny<CancellationToken>()))
                .ReturnsAsync((IngestionJob?)null);
        }
    }

    private void SetUpJobFound(string uploadId, IngestionJob job)
    {
        _jobRepoMock
            .Setup(x => x.GetLatestByInputAsync(uploadId, IngestionJobType.PDFManual, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _jobRepoMock
            .Setup(x => x.GetLatestByInputAsync(uploadId, IngestionJobType.StructuredSpecification, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IngestionJob?)null);
        _jobRepoMock
            .Setup(x => x.GetLatestByInputAsync(uploadId, IngestionJobType.Batch, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IngestionJob?)null);
    }

    [Fact]
    public async Task UploadArtifactAsync_InvalidUploadId_ReturnsInvalidUploadIdStatus()
    {
        var request = new ProcessorArtifactUploadRequest("not-a-guid", "search-chunks", new MemoryStream(), "application/jsonl");

        var result = await _sut.UploadArtifactAsync(request);

        result.Status.Should().Be(ProcessorArtifactOperationStatus.InvalidUploadId);
        result.Response.Should().BeNull();
        _blobStorageMock.Verify(x => x.UploadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UploadArtifactAsync_InvalidArtifactType_ReturnsInvalidArtifactTypeStatus()
    {
        var uploadId = Guid.NewGuid().ToString();
        var request = new ProcessorArtifactUploadRequest(uploadId, "unknown-type", new MemoryStream(), "application/octet-stream");

        var result = await _sut.UploadArtifactAsync(request);

        result.Status.Should().Be(ProcessorArtifactOperationStatus.InvalidArtifactType);
        result.Response.Should().BeNull();
        _blobStorageMock.Verify(x => x.UploadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UploadArtifactAsync_GraphEntitiesArtifact_UsesRawUploadsContainerAndSkipsChunkIndexing()
    {
        var uploadId = Guid.NewGuid().ToString();
        var request = new ProcessorArtifactUploadRequest(uploadId, "graph-entities", new MemoryStream(new byte[] { 1, 2, 3 }), "application/json");

        var result = await _sut.UploadArtifactAsync(request);

        result.Status.Should().Be(ProcessorArtifactOperationStatus.Success);
        result.Response.Should().NotBeNull();
        result.Response!.Status.Should().Be("stored");
        _blobStorageMock.Verify(
            x => x.UploadAsync("raw-uploads", $"graph-entities/{uploadId}/entities.json", It.IsAny<Stream>(), "application/json", It.IsAny<CancellationToken>()),
            Times.Once);
        _chunkIndexingMock.Verify(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UploadArtifactAsync_SearchChunksArtifact_NonSeekableContent_ThrowsInvalidOperationException()
    {
        var uploadId = Guid.NewGuid().ToString();
        var request = new ProcessorArtifactUploadRequest(uploadId, "search-chunks", new NonSeekableStream(), "application/jsonl");

        var act = async () => await _sut.UploadArtifactAsync(request);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Search chunk artifact content must be seekable for indexing.");
        _chunkIndexingMock.Verify(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UploadArtifactAsync_SearchChunksArtifact_AllChunksIndexed_UpsertsCompletedArtifactAndTransitionsJobCompleted()
    {
        var uploadId = Guid.NewGuid().ToString();
        var job = IngestionJob.Create(IngestionJobType.PDFManual, uploadId, createdBySubject: null);
        SetUpJobFound(uploadId, job);

        var outcomes = new List<ChunkIndexOutcome>
        {
            new("chunk-1", true, null, 1, 0, "file.pdf"),
            new("chunk-2", true, null, 1, 1, "file.pdf")
        };
        _chunkIndexingMock
            .Setup(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), uploadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChunkIndexingResult(2, 1, outcomes));
        _jobRepoMock
            .Setup(x => x.TryTransitionToTerminalAsync(job.IngestionJobId, It.IsAny<IngestionJobStatus>(), IngestionJobStatus.Completed, 2, 2, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var request = new ProcessorArtifactUploadRequest(uploadId, "search-chunks", new MemoryStream(new byte[] { 1 }), "application/jsonl");

        var result = await _sut.UploadArtifactAsync(request);

        result.Status.Should().Be(ProcessorArtifactOperationStatus.Success);
        _artifactRepoMock.Verify(
            x => x.UpsertAsync(
                It.Is<IndexedArtifactDto>(a => a.State == IndexedArtifactState.Completed && a.IndexedChunkCount == 2 && a.FailedChunkCount == 0 && a.FailureReason == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _chunkRepoMock.Verify(x => x.DeleteByArtifactIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
        _chunkRepoMock.Verify(
            x => x.UpsertManyAsync(It.Is<IReadOnlyCollection<IndexedChunkDto>>(c => c.Count == 2), It.IsAny<CancellationToken>()),
            Times.Once);
        _jobRepoMock.Verify(
            x => x.TryTransitionToTerminalAsync(job.IngestionJobId, It.IsAny<IngestionJobStatus>(), IngestionJobStatus.Completed, 2, 2, null, It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
        _blobStorageMock.Verify(
            x => x.SetMetadataAsync("search-chunks", $"{uploadId}/chunks.jsonl", It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UploadArtifactAsync_SearchChunksArtifact_PartialIndexing_MarksPartiallyIndexedAndTransitionsJob()
    {
        var uploadId = Guid.NewGuid().ToString();
        var job = IngestionJob.Create(IngestionJobType.PDFManual, uploadId, createdBySubject: null);
        SetUpJobFound(uploadId, job);

        var outcomes = new List<ChunkIndexOutcome>
        {
            new("chunk-1", true, null, 1, 0, "file.pdf"),
            new("chunk-2", false, "embedding failure", 1, 1, "file.pdf")
        };
        _chunkIndexingMock
            .Setup(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), uploadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChunkIndexingResult(2, 1, outcomes));
        _jobRepoMock
            .Setup(x => x.TryTransitionToTerminalAsync(job.IngestionJobId, It.IsAny<IngestionJobStatus>(), IngestionJobStatus.PartiallyCompleted, 2, 1, "Failed to index 1 chunk(s)", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var request = new ProcessorArtifactUploadRequest(uploadId, "search-chunks", new MemoryStream(new byte[] { 1 }), "application/jsonl");

        var result = await _sut.UploadArtifactAsync(request);

        result.Status.Should().Be(ProcessorArtifactOperationStatus.Success);
        _artifactRepoMock.Verify(
            x => x.UpsertAsync(
                It.Is<IndexedArtifactDto>(a => a.State == IndexedArtifactState.PartiallyIndexed && a.IndexedChunkCount == 1 && a.FailedChunkCount == 1 && a.FailureReason == "Failed to index 1 chunk(s)"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _chunkRepoMock.Verify(
            x => x.UpsertManyAsync(It.Is<IReadOnlyCollection<IndexedChunkDto>>(c => c.Count == 2), It.IsAny<CancellationToken>()),
            Times.Once);
        _jobRepoMock.Verify(
            x => x.TryTransitionToTerminalAsync(job.IngestionJobId, It.IsAny<IngestionJobStatus>(), IngestionJobStatus.PartiallyCompleted, 2, 1, "Failed to index 1 chunk(s)", It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task UploadArtifactAsync_SearchChunksArtifact_NoJobFound_SkipsCatalogWriteButStillSetsBlobMetadata()
    {
        var uploadId = Guid.NewGuid().ToString();
        SetUpNoJobFound(uploadId);

        var outcomes = new List<ChunkIndexOutcome> { new("chunk-1", true, null, 1, 0, "file.pdf") };
        _chunkIndexingMock
            .Setup(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), uploadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChunkIndexingResult(1, 1, outcomes));

        var request = new ProcessorArtifactUploadRequest(uploadId, "search-chunks", new MemoryStream(new byte[] { 1 }), "application/jsonl");

        var result = await _sut.UploadArtifactAsync(request);

        result.Status.Should().Be(ProcessorArtifactOperationStatus.Success);
        _artifactRepoMock.Verify(x => x.UpsertAsync(It.IsAny<IndexedArtifactDto>(), It.IsAny<CancellationToken>()), Times.Never);
        _chunkRepoMock.Verify(x => x.DeleteByArtifactIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _chunkRepoMock.Verify(x => x.UpsertManyAsync(It.IsAny<IReadOnlyCollection<IndexedChunkDto>>(), It.IsAny<CancellationToken>()), Times.Never);
        _jobRepoMock.Verify(x => x.TryTransitionToTerminalAsync(It.IsAny<Guid>(), It.IsAny<IngestionJobStatus>(), It.IsAny<IngestionJobStatus>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        _blobStorageMock.Verify(
            x => x.SetMetadataAsync("search-chunks", $"{uploadId}/chunks.jsonl", It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UploadArtifactAsync_SearchChunksArtifact_MetadataUpdateFails_DoesNotThrowAndUploadStillSucceeds()
    {
        var uploadId = Guid.NewGuid().ToString();
        SetUpNoJobFound(uploadId);

        var outcomes = new List<ChunkIndexOutcome> { new("chunk-1", true, null, 1, 0, "file.pdf") };
        _chunkIndexingMock
            .Setup(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), uploadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChunkIndexingResult(1, 1, outcomes));
        _blobStorageMock
            .Setup(x => x.SetMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("metadata store unavailable"));

        var request = new ProcessorArtifactUploadRequest(uploadId, "search-chunks", new MemoryStream(new byte[] { 1 }), "application/jsonl");

        var act = async () => await _sut.UploadArtifactAsync(request);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task UploadArtifactAsync_SearchChunksArtifact_IndexingThrows_TransitionsJobToFailed()
    {
        var uploadId = Guid.NewGuid().ToString();
        var job = IngestionJob.Create(IngestionJobType.PDFManual, uploadId, createdBySubject: null);
        SetUpJobFound(uploadId, job);

        _chunkIndexingMock
            .Setup(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), uploadId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("search index unavailable"));
        _jobRepoMock
            .Setup(x => x.TryTransitionToTerminalAsync(job.IngestionJobId, It.IsAny<IngestionJobStatus>(), IngestionJobStatus.Failed, 0, 0, It.Is<string>(s => s.Contains("search index unavailable")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var request = new ProcessorArtifactUploadRequest(uploadId, "search-chunks", new MemoryStream(new byte[] { 1 }), "application/jsonl");

        var result = await _sut.UploadArtifactAsync(request);

        // The exception is caught internally by ProcessSearchChunksAsync (best-effort); the upload itself still reports success.
        result.Status.Should().Be(ProcessorArtifactOperationStatus.Success);
        _jobRepoMock.Verify(
            x => x.TryTransitionToTerminalAsync(job.IngestionJobId, It.IsAny<IngestionJobStatus>(), IngestionJobStatus.Failed, 0, 0, It.Is<string>(s => s.Contains("search index unavailable")), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task UploadArtifactAsync_SearchChunksArtifact_IndexingThrowsAndNoJobFound_DoesNotThrowAndSkipsTransition()
    {
        var uploadId = Guid.NewGuid().ToString();
        SetUpNoJobFound(uploadId);

        _chunkIndexingMock
            .Setup(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), uploadId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("search index unavailable"));

        var request = new ProcessorArtifactUploadRequest(uploadId, "search-chunks", new MemoryStream(new byte[] { 1 }), "application/jsonl");

        var act = async () => await _sut.UploadArtifactAsync(request);

        await act.Should().NotThrowAsync();
        _jobRepoMock.Verify(
            x => x.TryTransitionToTerminalAsync(It.IsAny<Guid>(), It.IsAny<IngestionJobStatus>(), It.IsAny<IngestionJobStatus>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UploadArtifactAsync_SearchChunksArtifact_IndexingThrowsAndTransitionAlsoThrows_SwallowsBothExceptions()
    {
        var uploadId = Guid.NewGuid().ToString();
        var job = IngestionJob.Create(IngestionJobType.PDFManual, uploadId, createdBySubject: null);
        SetUpJobFound(uploadId, job);

        _chunkIndexingMock
            .Setup(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), uploadId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("search index unavailable"));
        _jobRepoMock
            .Setup(x => x.TryTransitionToTerminalAsync(It.IsAny<Guid>(), It.IsAny<IngestionJobStatus>(), It.IsAny<IngestionJobStatus>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("sql unavailable"));

        var request = new ProcessorArtifactUploadRequest(uploadId, "search-chunks", new MemoryStream(new byte[] { 1 }), "application/jsonl");

        var act = async () => await _sut.UploadArtifactAsync(request);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ReportJobStageByRunIdAsync_ValidRunIdAndRequest_DelegatesToTransitionStageAsync()
    {
        var runId = Guid.NewGuid().ToString();
        var job = IngestionJob.Create(IngestionJobType.PDFManual, runId, createdBySubject: null);
        var request = new IngestionJobStageRequest { Stage = "embedding", ChunksProcessed = 5, TotalChunks = 10 };
        var response = new IngestionJobStatusResponse { Status = "Processing", CurrentStage = "embedding" };

        _jobRepoMock
            .Setup(x => x.GetByDocIngestionRunIdAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _ingestionJobServiceMock
            .Setup(x => x.TransitionStageAsync(job.IngestionJobId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _sut.ReportJobStageByRunIdAsync(runId, request);

        result.Should().BeSameAs(response);
        _ingestionJobServiceMock.Verify(
            x => x.TransitionStageAsync(job.IngestionJobId, request, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ReportJobStageByRunIdAsync_RunIdNotFound_ReturnsNull()
    {
        var runId = Guid.NewGuid().ToString();
        var request = new IngestionJobStageRequest { Stage = "embedding" };

        _jobRepoMock
            .Setup(x => x.GetByDocIngestionRunIdAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IngestionJob?)null);

        var result = await _sut.ReportJobStageByRunIdAsync(runId, request);

        result.Should().BeNull();
        _ingestionJobServiceMock.Verify(
            x => x.TransitionStageAsync(It.IsAny<Guid>(), It.IsAny<IngestionJobStageRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ReportJobStageByRunIdAsync_NullRunId_ThrowsArgumentException()
    {
        var request = new IngestionJobStageRequest { Stage = "embedding" };

        var act = async () => await _sut.ReportJobStageByRunIdAsync(null!, request);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ReportJobStageByRunIdAsync_EmptyRunId_ThrowsArgumentException()
    {
        var request = new IngestionJobStageRequest { Stage = "embedding" };

        var act = async () => await _sut.ReportJobStageByRunIdAsync("", request);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ReportJobStageByRunIdAsync_WhitespaceRunId_ThrowsArgumentException()
    {
        var request = new IngestionJobStageRequest { Stage = "embedding" };

        var act = async () => await _sut.ReportJobStageByRunIdAsync("   ", request);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ReportJobStageByRunIdAsync_NullRequest_ThrowsArgumentNullException()
    {
        var runId = Guid.NewGuid().ToString();

        var act = async () => await _sut.ReportJobStageByRunIdAsync(runId, null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ReportJobStageByRunIdAsync_JobFound_PassesCancellationToken()
    {
        var runId = Guid.NewGuid().ToString();
        var job = IngestionJob.Create(IngestionJobType.PDFManual, runId, createdBySubject: null);
        var request = new IngestionJobStageRequest { Stage = "embedding" };
        var response = new IngestionJobStatusResponse();
        using var cts = new CancellationTokenSource();

        _jobRepoMock
            .Setup(x => x.GetByDocIngestionRunIdAsync(runId, cts.Token))
            .ReturnsAsync(job);
        _ingestionJobServiceMock
            .Setup(x => x.TransitionStageAsync(job.IngestionJobId, request, cts.Token))
            .ReturnsAsync(response);

        var result = await _sut.ReportJobStageByRunIdAsync(runId, request, cts.Token);

        result.Should().NotBeNull();
    }
}
