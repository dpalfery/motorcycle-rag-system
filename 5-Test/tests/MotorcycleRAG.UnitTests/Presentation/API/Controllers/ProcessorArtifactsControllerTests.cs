using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MotorcycleRAG.API.Controllers;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.UnitTests.Presentation.API.Controllers;

public sealed class ProcessorArtifactsControllerTests
{
    [Fact]
    public async Task UploadArtifactAsync_WithSearchChunks_AttachesToStructuredSpecificationJob()
    {
        var uploadId = Guid.NewGuid().ToString();
        var jobId = Guid.NewGuid();
        var job = new IngestionJob
        {
            IngestionJobId = jobId,
            InputRef = uploadId,
            InputType = IngestionJobType.StructuredSpecification,
            Status = IngestionJobStatus.Processing,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        var blobStorage = CreateBlobStorage();
        var indexingService = CreateSuccessfulIndexingService(uploadId);
        var jobRepository = new Mock<IIngestionJobRepository>();
        var artifactRepository = CreateArtifactRepository();
        var chunkRepository = CreateChunkRepository();

        jobRepository
            .Setup(repository => repository.GetLatestByInputAsync(uploadId, IngestionJobType.PDFManual, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IngestionJob?)null);
        jobRepository
            .Setup(repository => repository.GetLatestByInputAsync(uploadId, IngestionJobType.StructuredSpecification, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        jobRepository
            .Setup(repository => repository.GetLatestByInputAsync(uploadId, IngestionJobType.Batch, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IngestionJob?)null);
        jobRepository
            .Setup(repository => repository.TryTransitionToTerminalAsync(
                jobId,
                IngestionJobStatus.Processing,
                IngestionJobStatus.Completed,
                1,
                1,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = CreateController(
            blobStorage.Object,
            indexingService.Object,
            jobRepository.Object,
            artifactRepository.Object,
            chunkRepository.Object);

        var file = CreateFormFile("{\"id\":\"chunk-1\"}\n", "chunks.jsonl", "application/x-ndjson");

        var result = await sut.UploadArtifactAsync(file, uploadId, "search-chunks", CancellationToken.None);

        var accepted = result.Should().BeOfType<AcceptedResult>().Subject;
        accepted.Value.Should().BeOfType<ProcessorArtifactUploadResponse>()
            .Which.BlobPath.Should().Be($"{uploadId}/chunks.jsonl");

        jobRepository.Verify(repository => repository.GetLatestByInputAsync(uploadId, IngestionJobType.PDFManual, It.IsAny<CancellationToken>()), Times.Once);
        jobRepository.Verify(repository => repository.GetLatestByInputAsync(uploadId, IngestionJobType.StructuredSpecification, It.IsAny<CancellationToken>()), Times.Once);
        jobRepository.Verify(repository => repository.TryTransitionToTerminalAsync(
            jobId,
            IngestionJobStatus.Indexing,
            IngestionJobStatus.Completed,
            1,
            1,
            null,
            It.IsAny<CancellationToken>()), Times.Once);
        jobRepository.Verify(repository => repository.TryTransitionToTerminalAsync(
            jobId,
            IngestionJobStatus.Processing,
            IngestionJobStatus.Completed,
            1,
            1,
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadArtifactAsync_WithGraphEntities_StoresInRawUploadsWithoutIndexing()
    {
        var uploadId = Guid.NewGuid().ToString();
        var blobStorage = CreateBlobStorage();
        var indexingService = new Mock<IChunkIndexingService>();

        var sut = CreateController(
            blobStorage.Object,
            indexingService.Object,
            Mock.Of<IIngestionJobRepository>(),
            Mock.Of<IIndexedArtifactRepository>(),
            Mock.Of<IIndexedChunkRepository>());

        var file = CreateFormFile("[{\"nodes\":[],\"edges\":[]}]", "entities.json", "application/json");

        var result = await sut.UploadArtifactAsync(file, uploadId, "graph-entities", CancellationToken.None);

        result.Should().BeOfType<AcceptedResult>();
        blobStorage.Verify(service => service.UploadAsync(
            "raw-uploads",
            $"graph-entities/{uploadId}/entities.json",
            It.IsAny<Stream>(),
            "application/json",
            It.IsAny<CancellationToken>()), Times.Once);
        indexingService.Verify(service => service.IndexFromJsonlAsync(
            It.IsAny<Stream>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static ProcessorArtifactsController CreateController(
        IBlobStorageService blobStorageService,
        IChunkIndexingService chunkIndexingService,
        IIngestionJobRepository jobRepository,
        IIndexedArtifactRepository artifactRepository,
        IIndexedChunkRepository chunkRepository)
    {
        return new ProcessorArtifactsController(
            blobStorageService,
            Options.Create(new BlobStorageOptions
            {
                RawUploadsContainer = "raw-uploads"
            }),
            chunkIndexingService,
            jobRepository,
            artifactRepository,
            chunkRepository,
            NullLogger<ProcessorArtifactsController>.Instance);
    }

    private static Mock<IBlobStorageService> CreateBlobStorage()
    {
        var blobStorage = new Mock<IBlobStorageService>();
        blobStorage
            .Setup(service => service.UploadAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://storage.example/artifact");
        blobStorage
            .Setup(service => service.SetMetadataAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return blobStorage;
    }

    private static Mock<IChunkIndexingService> CreateSuccessfulIndexingService(string uploadId)
    {
        var indexingService = new Mock<IChunkIndexingService>();
        indexingService
            .Setup(service => service.IndexFromJsonlAsync(
                It.IsAny<Stream>(),
                uploadId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChunkIndexingResult(
                1,
                [
                    new ChunkIndexOutcome(
                        "chunk-1",
                        true,
                        null,
                        7,
                        0,
                        "source.pdf")
                ]));

        return indexingService;
    }

    private static Mock<IIndexedArtifactRepository> CreateArtifactRepository()
    {
        var artifactRepository = new Mock<IIndexedArtifactRepository>();
        artifactRepository
            .Setup(repository => repository.UpsertAsync(It.IsAny<IndexedArtifact>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IndexedArtifact artifact, CancellationToken _) => artifact);
        return artifactRepository;
    }

    private static Mock<IIndexedChunkRepository> CreateChunkRepository()
    {
        var chunkRepository = new Mock<IIndexedChunkRepository>();
        chunkRepository
            .Setup(repository => repository.DeleteByArtifactIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        chunkRepository
            .Setup(repository => repository.UpsertManyAsync(It.IsAny<IReadOnlyCollection<IndexedChunk>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return chunkRepository;
    }

    private static IFormFile CreateFormFile(string content, string fileName, string contentType)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, stream.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }
}
