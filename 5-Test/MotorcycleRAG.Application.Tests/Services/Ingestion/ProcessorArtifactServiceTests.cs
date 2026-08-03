using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    private readonly Mock<IManualDocumentRepository> _manualDocumentRepoMock;
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
        _manualDocumentRepoMock = new Mock<IManualDocumentRepository>();

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
            _manualDocumentRepoMock.Object,
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
    public async Task DownloadSourceAsync_WhenUploadIdIsInvalid_ReturnsInvalidUploadIdWithoutStorageAccess()
    {
        var result = await _sut.DownloadSourceAsync("not-a-guid", "manual-pdf", null);

        result.Status.Should().Be(ProcessorArtifactOperationStatus.InvalidUploadId);
        result.Content.Should().BeNull();
        _blobStorageMock.Verify(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DownloadSourceAsync_WhenDocumentTypeIsInvalid_ReturnsInvalidDocumentTypeWithoutStorageAccess()
    {
        var result = await _sut.DownloadSourceAsync(Guid.NewGuid().ToString(), "unsupported", null);

        result.Status.Should().Be(ProcessorArtifactOperationStatus.InvalidDocumentType);
        result.ContentType.Should().BeNull();
        _blobStorageMock.Verify(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DownloadSourceAsync_WhenAccessTokenIsInvalid_ReturnsUnauthorizedWithoutStorageAccess()
    {
        var uploadId = Guid.NewGuid().ToString();
        _tokenServiceMock
            .Setup(x => x.IsValid("bad-token", uploadId, "manual-pdf"))
            .Returns(false);

        var result = await _sut.DownloadSourceAsync(uploadId, "manual-pdf", "bad-token");

        result.Status.Should().Be(ProcessorArtifactOperationStatus.Unauthorized);
        _tokenServiceMock.Verify(x => x.IsValid("bad-token", uploadId, "manual-pdf"), Times.Once);
        _blobStorageMock.Verify(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DownloadSourceAsync_WhenSourceDoesNotExist_ReturnsNotFound()
    {
        var uploadId = Guid.NewGuid().ToString();
        _blobStorageMock
            .Setup(x => x.ExistsAsync("raw-uploads", $"{uploadId}/source.pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _sut.DownloadSourceAsync(uploadId, "manual-pdf", null);

        result.Status.Should().Be(ProcessorArtifactOperationStatus.NotFound);
        _blobStorageMock.Verify(x => x.DownloadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DownloadSourceAsync_WhenSourceExists_ReturnsDownloadedStreamAndContentType()
    {
        var uploadId = Guid.NewGuid().ToString();
        var source = new MemoryStream([1, 2, 3]);
        using var cts = new CancellationTokenSource();
        _blobStorageMock
            .Setup(x => x.ExistsAsync("raw-uploads", $"{uploadId}/source.pdf", cts.Token))
            .ReturnsAsync(true);
        _blobStorageMock
            .Setup(x => x.DownloadAsync("raw-uploads", $"{uploadId}/source.pdf", cts.Token))
            .ReturnsAsync(source);

        var result = await _sut.DownloadSourceAsync(uploadId, "manual-pdf", null, cts.Token);

        result.Status.Should().Be(ProcessorArtifactOperationStatus.Success);
        result.ContentType.Should().Be("application/pdf");
        result.Content.Should().BeSameAs(source);
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
        _chunkIndexingMock.Verify(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UploadArtifactAsync_SearchChunksArtifact_NonSeekableContent_ThrowsInvalidOperationException()
    {
        var uploadId = Guid.NewGuid().ToString();
        var request = new ProcessorArtifactUploadRequest(uploadId, "search-chunks", new NonSeekableStream(), "application/jsonl");

        var act = async () => await _sut.UploadArtifactAsync(request);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Search chunk artifact content must be seekable for indexing.");
        _chunkIndexingMock.Verify(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
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
            .Setup(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), uploadId, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
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
            .Setup(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), uploadId, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
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
    public async Task UploadArtifactAsync_SearchChunksArtifact_WhenIndexingTransitionNeedsProcessingStatus_ContinuesUntilTransitioned()
    {
        var uploadId = Guid.NewGuid().ToString();
        var job = IngestionJob.Create(IngestionJobType.PDFManual, uploadId, createdBySubject: null);
        SetUpJobFound(uploadId, job);
        _chunkIndexingMock
            .Setup(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), uploadId, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChunkIndexingResult(1, 1, [new ChunkIndexOutcome("chunk-1", true, null, 1, 0, "file.pdf")]));
        _jobRepoMock
            .Setup(x => x.TryTransitionToTerminalAsync(job.IngestionJobId, IngestionJobStatus.Indexing, IngestionJobStatus.Completed, 1, 1, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _jobRepoMock
            .Setup(x => x.TryTransitionToTerminalAsync(job.IngestionJobId, IngestionJobStatus.Processing, IngestionJobStatus.Completed, 1, 1, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _sut.UploadArtifactAsync(new ProcessorArtifactUploadRequest(uploadId, "search-chunks", new MemoryStream([1]), "application/jsonl"));

        result.Status.Should().Be(ProcessorArtifactOperationStatus.Success);
        _jobRepoMock.Verify(x => x.TryTransitionToTerminalAsync(job.IngestionJobId, IngestionJobStatus.Indexing, IngestionJobStatus.Completed, 1, 1, null, It.IsAny<CancellationToken>()), Times.Once);
        _jobRepoMock.Verify(x => x.TryTransitionToTerminalAsync(job.IngestionJobId, IngestionJobStatus.Processing, IngestionJobStatus.Completed, 1, 1, null, It.IsAny<CancellationToken>()), Times.Once);
        _jobRepoMock.Verify(x => x.TryTransitionToTerminalAsync(job.IngestionJobId, IngestionJobStatus.Queued, IngestionJobStatus.Completed, 1, 1, null, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UploadArtifactAsync_SearchChunksArtifact_WhenNoActiveStateTransitions_ExhaustsAllTerminalTransitions()
    {
        var uploadId = Guid.NewGuid().ToString();
        var job = IngestionJob.Create(IngestionJobType.PDFManual, uploadId, createdBySubject: null);
        SetUpJobFound(uploadId, job);
        _chunkIndexingMock
            .Setup(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), uploadId, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChunkIndexingResult(1, 1, [new ChunkIndexOutcome("chunk-1", true, null, 1, 0, "file.pdf")]));
        _jobRepoMock
            .Setup(x => x.TryTransitionToTerminalAsync(job.IngestionJobId, It.IsAny<IngestionJobStatus>(), IngestionJobStatus.Completed, 1, 1, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _sut.UploadArtifactAsync(new ProcessorArtifactUploadRequest(uploadId, "search-chunks", new MemoryStream([1]), "application/jsonl"));

        result.Status.Should().Be(ProcessorArtifactOperationStatus.Success);
        _jobRepoMock.Verify(x => x.TryTransitionToTerminalAsync(job.IngestionJobId, It.IsAny<IngestionJobStatus>(), IngestionJobStatus.Completed, 1, 1, null, It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task UploadArtifactAsync_SearchChunksArtifact_NoJobFound_SkipsCatalogWriteAndSkipsBlobMetadata()
    {
        var uploadId = Guid.NewGuid().ToString();
        SetUpNoJobFound(uploadId);

        // D3: null job → no anchors → no indexing → no artifactState → SetMetadataAsync is not invoked.
        // Indexing and catalog operations are never reached in the no-job path post-T8.

        var request = new ProcessorArtifactUploadRequest(uploadId, "search-chunks", new MemoryStream(new byte[] { 1 }), "application/jsonl");

        var result = await _sut.UploadArtifactAsync(request);

        result.Status.Should().Be(ProcessorArtifactOperationStatus.Success);
        _artifactRepoMock.Verify(x => x.UpsertAsync(It.IsAny<IndexedArtifactDto>(), It.IsAny<CancellationToken>()), Times.Never);
        _chunkRepoMock.Verify(x => x.DeleteByArtifactIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _chunkRepoMock.Verify(x => x.UpsertManyAsync(It.IsAny<IReadOnlyCollection<IndexedChunkDto>>(), It.IsAny<CancellationToken>()), Times.Never);
        _jobRepoMock.Verify(x => x.TryTransitionToTerminalAsync(It.IsAny<Guid>(), It.IsAny<IngestionJobStatus>(), It.IsAny<IngestionJobStatus>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        _blobStorageMock.Verify(
            x => x.SetMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UploadArtifactAsync_SearchChunksArtifact_MetadataUpdateFails_DoesNotThrowAndUploadStillSucceeds()
    {
        var uploadId = Guid.NewGuid().ToString();
        var job = IngestionJob.Create(IngestionJobType.PDFManual, uploadId, createdBySubject: null);
        SetUpJobFound(uploadId, job);

        var outcomes = new List<ChunkIndexOutcome> { new("chunk-1", true, null, 1, 0, "file.pdf") };
        _chunkIndexingMock
            .Setup(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), uploadId, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChunkIndexingResult(1, 1, outcomes));
        _jobRepoMock
            .Setup(x => x.TryTransitionToTerminalAsync(job.IngestionJobId, It.IsAny<IngestionJobStatus>(), IngestionJobStatus.Completed, 1, 1, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _blobStorageMock
            .Setup(x => x.SetMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("metadata store unavailable"));

        var request = new ProcessorArtifactUploadRequest(uploadId, "search-chunks", new MemoryStream(new byte[] { 1 }), "application/jsonl");

        var act = async () => await _sut.UploadArtifactAsync(request);

        await act.Should().NotThrowAsync();
        _blobStorageMock.Verify(
            x => x.SetMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UploadArtifactAsync_SearchChunksArtifact_IndexingThrows_TransitionsJobToFailed()
    {
        var uploadId = Guid.NewGuid().ToString();
        var job = IngestionJob.Create(IngestionJobType.PDFManual, uploadId, createdBySubject: null);
        SetUpJobFound(uploadId, job);

        _chunkIndexingMock
            .Setup(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), uploadId, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
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
            .Setup(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), uploadId, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
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
            .Setup(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), uploadId, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("search index unavailable"));
        _jobRepoMock
            .Setup(x => x.TryTransitionToTerminalAsync(It.IsAny<Guid>(), It.IsAny<IngestionJobStatus>(), It.IsAny<IngestionJobStatus>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("sql unavailable"));

        var request = new ProcessorArtifactUploadRequest(uploadId, "search-chunks", new MemoryStream(new byte[] { 1 }), "application/jsonl");

        var act = async () => await _sut.UploadArtifactAsync(request);

        await act.Should().NotThrowAsync();
    }

    // --- T8 (plan: 2026-08-01-vector-graph-anchor-id-contract, decision D3) ---
    // ProcessSearchChunksAsync must resolve indexedArtifactId/ingestionJobId/sourceContentHash BEFORE
    // calling IChunkIndexingService.IndexFromJsonlAsync, and must call the 5-parameter overload with
    // those resolved values -- not the legacy 3-parameter overload, which today's production code calls
    // and which only ever carries empty/default anchors. These tests mock IChunkIndexingService directly
    // (not the real ChunkIndexingService implementation), so the legacy overload's internal delegation to
    // the 5-parameter overload with Guid.Empty (a ChunkIndexingService implementation detail) is not in
    // play here -- a call to the mocked 3-parameter overload can never vacuously satisfy an assertion
    // written against the mocked 5-parameter overload's captured arguments.

    [Fact]
    public async Task UploadArtifactAsync_SearchChunksArtifact_ResolvesAnchorsBeforeIndexing_PassesNonDefaultValuesToFiveParameterOverload()
    {
        var uploadId = Guid.NewGuid().ToString();

        // Set up the ManualDocument mock with a SourceContentHash
        var testHash = "sha256-abc123def456";
        var testDocId = Guid.NewGuid();
        var manualDocument = ManualDocument.Create(
            documentId: testDocId,
            sourceFileName: "test.pdf",
            canonicalBlobContainer: "container",
            canonicalBlobPath: "path",
            documentType: "manual-pdf",
            sourceContentHash: testHash);

        // Create a job with an actual ManualDocumentId so it will resolve to the test document
        var job = IngestionJob.Rehydrate(
            id: 0,
            ingestionJobId: Guid.NewGuid(),
            createdAtUtc: DateTimeOffset.UtcNow,
            startedAtUtc: null,
            completedAtUtc: null,
            createdBySubject: null,
            status: IngestionJobStatus.Queued,
            failureReason: null,
            errorsJson: null,
            errorMessage: null,
            inputType: IngestionJobType.PDFManual,
            inputRef: uploadId,
            sourceFileName: null,
            computeProvider: "MicrosoftFabric",
            docIngestionRunId: null,
            manualDocumentId: testDocId,  // Set the ManualDocumentId to match our test document
            totalPages: null,
            pagesCapturedViewableCount: null,
            pagesWithSearchableTextCount: null,
            pagesWithOcrTextCount: null,
            pagesWithNativeTextCount: null,
            missingPagesJson: null,
            metricsJson: null,
            expectedChunkCount: null,
            indexedChunkCount: null,
            currentStage: null,
            stageSetAtUtc: null,
            metadataJson: null);

        SetUpJobFound(uploadId, job);

        _manualDocumentRepoMock
            .Setup(x => x.GetDocumentByIdAsync(testDocId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(manualDocument);

        var outcomes = new List<ChunkIndexOutcome>
        {
            new("chunk-1", true, null, 1, 0, "file.pdf"),
            new("chunk-2", true, null, 1, 1, "file.pdf")
        };
        var indexingResult = new ChunkIndexingResult(2, 1, outcomes);

        // GREEN production code must call the sole (5-parameter) IndexFromJsonlAsync overload, with the
        // anchors already resolved. Capture the arguments actually observed at call time -- not inferred
        // from the return value, per the test-contract row.
        // (T14 contract closure: the legacy 3-parameter overload referenced by an earlier revision of
        // this test has been removed entirely; the "exactly one overload" guarantee is now enforced
        // structurally by IndexFromJsonlAsync_ShouldExposeExactlyOneAnchorParameterOverload_OnInterfaceAndImplementations.)
        var fiveParameterOverloadInvoked = false;
        var capturedIndexedArtifactId = Guid.Empty;
        var capturedIngestionJobId = Guid.Empty;
        string? capturedSourceContentHash = null;

        _chunkIndexingMock
            .Setup(x => x.IndexFromJsonlAsync(
                It.IsAny<Stream>(),
                uploadId,
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<Stream, string, Guid, Guid, string, CancellationToken>((_, _, indexedArtifactId, ingestionJobId, sourceContentHash, _) =>
            {
                fiveParameterOverloadInvoked = true;
                capturedIndexedArtifactId = indexedArtifactId;
                capturedIngestionJobId = ingestionJobId;
                capturedSourceContentHash = sourceContentHash;
            })
            .ReturnsAsync(indexingResult);

        var request = new ProcessorArtifactUploadRequest(uploadId, "search-chunks", new MemoryStream(new byte[] { 1 }), "application/jsonl");

        var result = await _sut.UploadArtifactAsync(request);

        result.Status.Should().Be(ProcessorArtifactOperationStatus.Success);

        // The heart of the assertion: the sole (5-parameter) overload must be invoked, and it must
        // carry anchors that were already resolved -- not the Guid.Empty/null values a post-hoc merge
        // would leave behind.
        fiveParameterOverloadInvoked.Should().BeTrue(
            "ProcessSearchChunksAsync must resolve indexedArtifactId/ingestionJobId/sourceContentHash and call " +
            "the IndexFromJsonlAsync overload with them (plan decision D3) -- otherwise the anchors are never " +
            "attached to the vector write");
        capturedIndexedArtifactId.Should().NotBe(Guid.Empty,
            "the artifact ID must be pre-allocated (Guid.NewGuid()) before the index call, not left as the default");
        capturedIngestionJobId.Should().Be(job.IngestionJobId,
            "the ingestion job lookup must complete and its resolved ID must be passed into the index call, not resolved afterwards");
        capturedSourceContentHash.Should().NotBeNullOrWhiteSpace(
            "the owning ManualDocument.SourceContentHash must be resolved and passed into the index call");

        // Regression guard: the existing post-index persistence calls are unchanged in shape/values, AND
        // the persisted artifact/chunks reuse the SAME pre-allocated ID that was passed into the index
        // call above -- proving the anchor is a single resolved value, not two independently generated GUIDs.
        _artifactRepoMock.Verify(
            x => x.UpsertAsync(
                It.Is<IndexedArtifactDto>(a =>
                    a.IndexedArtifactId == capturedIndexedArtifactId &&
                    a.IngestionJobId == job.IngestionJobId &&
                    a.State == IndexedArtifactState.Completed &&
                    a.IndexedChunkCount == 2 &&
                    a.FailedChunkCount == 0 &&
                    a.FailureReason == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _chunkRepoMock.Verify(
            x => x.UpsertManyAsync(
                It.Is<IReadOnlyCollection<IndexedChunkDto>>(chunks =>
                    chunks.Count == 2 && chunks.All(chunk => chunk.IndexedArtifactId == capturedIndexedArtifactId)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // --- T5 (plan: 2026-08-02-anchor-id-debt-cleanup, D7) ---
    // Regression guard for the §7 null-clobber defense at the T8 (initial-ingestion) call site.
    // Mirrors ChunkReprocessServiceTests.ReprocessByJobIdAsync_JobHasNoManualDocument_PassesNullSourceContentHash_NeverEmptyString:
    // a freshly-created job (IngestionJob.Create always leaves ManualDocumentId null -- e.g. a
    // StructuredSpecification/Batch-shaped upload, or any PDFManual job whose manual link hasn't been
    // set) must resolve to a null sourceContentHash passed into IndexFromJsonlAsync, NEVER string.Empty.
    // An empty string would serialize as a real key and Azure AI Search mergeOrUpload would treat it as
    // "clear this field", silently wiping a real hash already indexed for those chunks.
    [Fact]
    public async Task UploadArtifactAsync_SearchChunksArtifact_JobHasNoManualDocument_PassesNullSourceContentHash_NeverEmptyString()
    {
        var uploadId = Guid.NewGuid().ToString();

        // IngestionJob.Create always leaves ManualDocumentId null -- the freshly-created-job shape.
        var job = IngestionJob.Create(IngestionJobType.PDFManual, uploadId, createdBySubject: null);
        SetUpJobFound(uploadId, job);

        var outcomes = new List<ChunkIndexOutcome> { new("chunk-1", true, null, 1, 0, "file.pdf") };
        var indexingResult = new ChunkIndexingResult(1, 1, outcomes);

        // Sentinel-initialize so a "never called" failure is distinguishable from a real null pass.
        string? capturedSourceContentHash = "unset-sentinel";
        _chunkIndexingMock
            .Setup(x => x.IndexFromJsonlAsync(
                It.IsAny<Stream>(),
                uploadId,
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback<Stream, string, Guid, Guid, string?, CancellationToken>((_, _, _, _, sourceContentHash, _) =>
            {
                capturedSourceContentHash = sourceContentHash;
            })
            .ReturnsAsync(indexingResult);

        var request = new ProcessorArtifactUploadRequest(uploadId, "search-chunks", new MemoryStream(new byte[] { 1 }), "application/jsonl");

        var result = await _sut.UploadArtifactAsync(request);

        result.Status.Should().Be(ProcessorArtifactOperationStatus.Success);
        capturedSourceContentHash.Should().BeNull(
            "a job with no ManualDocumentId must resolve to a null sourceContentHash, not an unresolved sentinel " +
            "or an empty string -- null is what the T7 JsonIgnore(WhenWritingNull) guard omits from the merge payload");
        capturedSourceContentHash.Should().NotBe(string.Empty,
            "string.Empty is NOT an acceptable substitute for null here -- mergeOrUpload still writes an empty " +
            "string as a real field value, clobbering any real hash already indexed for this chunk");
        _manualDocumentRepoMock.Verify(
            x => x.GetDocumentByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "when ManualDocumentId is null the helper must short-circuit and never consult the repository");
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
