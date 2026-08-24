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
    private readonly Mock<IManualDocumentRepository> _manualDocumentRepoMock;
    private readonly ChunkReprocessService _sut;

    public ChunkReprocessServiceTests()
    {
        _artifactRepoMock = new Mock<IIndexedArtifactRepository>();
        _chunkRepoMock = new Mock<IIndexedChunkRepository>();
        _jobRepoMock = new Mock<IIngestionJobRepository>();
        _blobStorageMock = new Mock<IBlobStorageService>();
        _indexingMock = new Mock<IChunkIndexingService>();
        _manualDocumentRepoMock = new Mock<IManualDocumentRepository>();

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
            NullLogger<ChunkReprocessService>.Instance,
            _manualDocumentRepoMock.Object);
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
            .Setup(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), successfulJob.InputRef, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), cts.Token))
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
        _indexingMock.Setup(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), partialJob.InputRef, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChunkIndexingResult(2, 1, [new ChunkIndexOutcome("one", true, null, 1, 0, "manual.pdf"), new ChunkIndexOutcome("two", false, "failed", 1, 1, "manual.pdf")]));
        _indexingMock.Setup(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), failedJob.InputRef, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
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
            .Setup(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), job.InputRef, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
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
            .Setup(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), job.InputRef, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
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
            .Setup(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), job.InputRef, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
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
            .Setup(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), job.InputRef, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(indexingResult);

        var result = await _sut.ReprocessByJobIdAsync(jobId);

        result.Should().Be(new ReprocessResultDto(1, 0, 0, 1));
        artifact.State.Should().Be(IndexedArtifactState.Failed);
        artifact.FailureReason.Should().Be("No chunks were successfully indexed.");
        _chunkRepoMock.Verify(x => x.DeleteByArtifactIdAsync(artifact.IndexedArtifactId, It.IsAny<CancellationToken>()), Times.Once);
        _chunkRepoMock.Verify(x => x.UpsertManyAsync(It.IsAny<IReadOnlyCollection<IndexedChunkDto>>(), It.IsAny<CancellationToken>()), Times.Never);
        _jobRepoMock.Verify(x => x.UpdateStatusAsync(jobId, IngestionJobStatus.Failed, "Chunk indexing failed.", It.IsAny<CancellationToken>()), Times.Once);
    }

    // --- T13 (plan: 2026-08-01-vector-graph-anchor-id-contract, row T13) ---
    // ReprocessByJobIdAsync calls the sole IndexFromJsonlAsync overload with the real
    // artifact.IndexedArtifactId and jobId anchors -- both already in scope in that method and already
    // bound into IndexedChunkDto ~20 lines later -- plus the resolved ManualDocument.SourceContentHash
    // (null when the job has no ManualDocument -- never string.Empty, per the T8 sibling rule; an empty
    // string would still overwrite a real hash on Azure AI Search mergeOrUpload, whereas a null/omitted
    // key leaves it untouched).
    //
    // These tests mock IChunkIndexingService directly (the interface, not the concrete
    // ChunkIndexingService), capturing the arguments actually observed at call time -- not inferred from
    // the return value. T14 contract closure made IManualDocumentRepository a required constructor
    // parameter on ChunkReprocessService (was previously optional = null as a T13 test-migration seam),
    // which is what unblocks the positive-hash test below.

    [Fact]
    public async Task ReprocessByJobIdAsync_ArtifactAndJobResolve_CallsFiveParameterOverloadWithRealArtifactAndJobIdAnchors()
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

        // GREEN production code must call the sole (5-parameter) IndexFromJsonlAsync overload, with the
        // anchors already resolved. Capture the arguments actually observed at call time -- not inferred
        // from the return value -- per the test-contract row.
        // (T14 contract closure: the legacy 3-parameter overload referenced by an earlier revision of
        // this test has been removed entirely; the "exactly one overload" guarantee is now enforced
        // structurally by IndexFromJsonlAsync_ShouldExposeExactlyOneAnchorParameterOverload_OnInterfaceAndImplementations.)
        var fiveParameterOverloadInvoked = false;
        var capturedIndexedArtifactId = Guid.Empty;
        var capturedIngestionJobId = Guid.Empty;
        string? capturedSourceContentHash = "unset-sentinel";

        _indexingMock
            .Setup(x => x.IndexFromJsonlAsync(
                It.IsAny<Stream>(),
                job.InputRef,
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

        var result = await _sut.ReprocessByJobIdAsync(jobId);

        result.Should().Be(new ReprocessResultDto(1, 1, 0, 0));

        // The heart of the assertion: the sole IndexFromJsonlAsync overload must be invoked, carrying
        // the real artifact/job anchors -- not left unmigrated on the legacy 3-parameter path, which
        // is what silently clobbers real anchors on reprocess (see plan §7 null-clobber finding).
        fiveParameterOverloadInvoked.Should().BeTrue(
            "ReprocessByJobIdAsync must call the IndexFromJsonlAsync overload (plan T13) with the resolved " +
            "anchors -- otherwise reprocessed chunks never carry the vector<->graph anchors, and once T8 lands, " +
            "a reprocess of an already-anchored document would silently wipe those anchors back to null");
        capturedIndexedArtifactId.Should().Be(artifact.IndexedArtifactId,
            "the artifact row is re-fetched by uploadId+type before indexing, so its real IndexedArtifactId " +
            "is already in scope and must be passed, not Guid.Empty");
        capturedIngestionJobId.Should().Be(jobId,
            "the jobId parameter is already in scope and is the same value later bound into IndexedChunkDto " +
            "~20 lines below -- it must be passed here too, not Guid.Empty");
        capturedSourceContentHash.Should().BeNull(
            "this job (built via IngestionJob.Create) has no ManualDocumentId, so the resolved hash must be " +
            "null -- never string.Empty, which mergeOrUpload would still treat as a real value");
    }

    [Fact]
    public async Task ReprocessByJobIdAsync_JobHasNoManualDocument_PassesNullSourceContentHash_NeverEmptyString()
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

        var outcomes = new List<ChunkIndexOutcome> { new("chunk-1", true, null, 1, 0, "file.pdf") };
        var indexingResult = new ChunkIndexingResult(1, 1, outcomes);

        string? capturedSourceContentHash = "unset-sentinel";
        _indexingMock
            .Setup(x => x.IndexFromJsonlAsync(
                It.IsAny<Stream>(),
                job.InputRef,
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<Stream, string, Guid, Guid, string, CancellationToken>((_, _, _, _, sourceContentHash, _) =>
            {
                capturedSourceContentHash = sourceContentHash;
            })
            .ReturnsAsync(indexingResult);

        await _sut.ReprocessByJobIdAsync(jobId);

        // job.ManualDocumentId is null here (IngestionJob.Create always leaves it null for freshly-created
        // jobs -- e.g. StructuredSpecification/Batch-shaped jobs, or any PDFManual job whose manual link
        // hasn't been set), so the resolved hash must be an omitted/null value on the outgoing document --
        // never string.Empty, which mergeOrUpload would still treat as a real value that clobbers any
        // existing hash already stamped on the indexed document (see plan §7 null-clobber finding, and the
        // T8 sibling rule this mirrors).
        capturedSourceContentHash.Should().BeNull(
            "a job with no ManualDocument must resolve to a null sourceContentHash, not an unresolved sentinel " +
            "or an empty string -- null is what the T7 JsonIgnore(WhenWritingNull) guard omits from the payload");
        capturedSourceContentHash.Should().NotBe(string.Empty,
            "string.Empty is NOT an acceptable substitute for null here -- mergeOrUpload still writes an empty " +
            "string as a real field value, clobbering any real hash already indexed for this chunk");
    }

    // T13 positive-hash case (plan T13 hygiene follow-up, folded into T14 per the T13 reviewer's
    // recommendation): a job WITH a ManualDocumentId must resolve and pass the real
    // ManualDocument.SourceContentHash into the index call -- not null, and not string.Empty. This is the
    // counterpart to ReprocessByJobIdAsync_JobHasNoManualDocument_PassesNullSourceContentHash_NeverEmptyString:
    // together they pin both branches of the hash-resolution contract. Now authorable because T14 made
    // ChunkReprocessService's IManualDocumentRepository constructor parameter required (was previously
    // optional = null, with a T13-follow-up note in SourceContentHashResolver.ResolveAsync).
    [Fact]
    public async Task ReprocessByJobIdAsync_JobHasManualDocument_PassesResolvedSourceContentHash_NeverNull()
    {
        var testHash = "sha256-realhash-789xyz";
        var testDocId = Guid.NewGuid();
        var manualDocument = ManualDocument.Create(
            documentId: testDocId,
            sourceFileName: "test.pdf",
            canonicalBlobContainer: "container",
            canonicalBlobPath: "path",
            documentType: "manual-pdf",
            sourceContentHash: testHash);
        _manualDocumentRepoMock
            .Setup(x => x.GetDocumentByIdAsync(testDocId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(manualDocument);

        // Build a job whose ManualDocumentId points at the mocked ManualDocument. IngestionJob.Create
        // always leaves ManualDocumentId null, so use Rehydrate to set it explicitly.
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
            inputRef: "uploads/linked-manual",
            sourceFileName: null,
            computeProvider: "MicrosoftFabric",
            docIngestionRunId: null,
            manualDocumentId: testDocId,
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

        var outcomes = new List<ChunkIndexOutcome> { new("chunk-1", true, null, 1, 0, "file.pdf") };
        var indexingResult = new ChunkIndexingResult(1, 1, outcomes);

        string? capturedSourceContentHash = null;
        _indexingMock
            .Setup(x => x.IndexFromJsonlAsync(
                It.IsAny<Stream>(),
                job.InputRef,
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<Stream, string, Guid, Guid, string, CancellationToken>((_, _, _, _, sourceContentHash, _) =>
            {
                capturedSourceContentHash = sourceContentHash;
            })
            .ReturnsAsync(indexingResult);

        await _sut.ReprocessByJobIdAsync(jobId);

        capturedSourceContentHash.Should().Be(testHash,
            "a job with a ManualDocumentId must resolve the owning ManualDocument.SourceContentHash and pass " +
            "it into the index call -- this is what anchors reprocessed chunks back to their source document " +
            "version, and is the positive counterpart to the no-ManualDocument null-hash contract");
        _manualDocumentRepoMock.Verify(
            x => x.GetDocumentByIdAsync(testDocId, It.IsAny<CancellationToken>()),
            Times.Once,
            "SourceContentHashResolver.ResolveAsync must actually look up the ManualDocument when ManualDocumentId is set");
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
            .Setup(x => x.IndexFromJsonlAsync(It.IsAny<Stream>(), job.InputRef, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(indexingResult);

        var result = await _sut.ReprocessByJobIdAsync(jobId);

        result.Should().Be(new ReprocessResultDto(1, 0, 0, 1));
        artifact.State.Should().Be(IndexedArtifactState.Failed);
        _jobRepoMock.Verify(x => x.UpdateStatusAsync(jobId, IngestionJobStatus.Failed, "Chunk indexing failed.", It.IsAny<CancellationToken>()), Times.Once);
    }
}
