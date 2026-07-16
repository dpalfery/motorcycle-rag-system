using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MotorcycleRAG.Application.Services.Ingestion;
using MotorcycleRAG.Domain.Constants;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Contracts.Models.DTOs.ManualIngestion;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;
using DomainDocumentStatus = MotorcycleRAG.Domain.Enums.ManualDocumentStatus;
using DomainRunStatus = MotorcycleRAG.Contracts.Models.DTOs.ManualIngestion.ManualRunStatus;
using DomainStageStatus = MotorcycleRAG.Contracts.Models.DTOs.ManualIngestion.ManualStageStatus;
using ManualProcessingRun = MotorcycleRAG.Contracts.Models.DTOs.ManualIngestion.ManualRunDto;
using ManualProcessingStage = MotorcycleRAG.Contracts.Models.DTOs.ManualIngestion.ManualStageDto;

namespace MotorcycleRAG.UnitTests.Services.Ingestion;

public sealed class ManualIngestionServiceTests
{
    [Fact]
    public async Task RegisterAndReadDocuments_MapPersistedDocuments()
    {
        var fixture = CreateFixture();
        var document = CreateDocument();
        fixture.BlobStorage.Setup(service => service.UploadAsync("moto-manuals", It.IsAny<string>(), It.IsAny<Stream>(), "application/pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://storage.test/manual.pdf");
        fixture.Repository.Setup(repository => repository.CreateDocumentAsync(It.IsAny<ManualDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManualDocument value, CancellationToken _) => value);
        fixture.Repository.Setup(repository => repository.GetDocumentByIdAsync(document.DocumentId, It.IsAny<CancellationToken>())).ReturnsAsync(document);
        fixture.Repository.Setup(repository => repository.GetAllDocumentsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([document]);

#pragma warning disable CA2025 // MemoryStream awaited before disposal; analyzer is overly conservative here
        using var stream = new MemoryStream([1, 2, 3]);
        var registered = await fixture.Sut.RegisterManualAsync(new RegisterManualDocumentRequest("manual.pdf", "service", "Honda", "CB", 2024, "hash"), stream, default);
        var loaded = await fixture.Sut.GetDocumentAsync(document.DocumentId, default);
        var all = await fixture.Sut.GetAllDocumentsAsync(default);

        registered.SourceFileName.Should().Be("manual.pdf");
        registered.CanonicalBlobUri.Should().Be(new Uri("https://storage.test/manual.pdf"));
        loaded.Should().NotBeNull();
        all.Should().ContainSingle();
        fixture.BlobStorage.Verify(service => service.UploadAsync("moto-manuals", It.Is<string>(value => value.EndsWith("/manual.pdf")), stream, "application/pdf", default), Times.Once);
#pragma warning restore CA2025
    }

    [Fact]
    public async Task GetDocumentAndRun_WhenMissing_ReturnNull()
    {
        var fixture = CreateFixture();
        fixture.Repository.Setup(repository => repository.GetDocumentByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((ManualDocument?)null);
        fixture.Repository.Setup(repository => repository.GetRunByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((ManualProcessingRun?)null);

        (await fixture.Sut.GetDocumentAsync(Guid.NewGuid(), default)).Should().BeNull();
        (await fixture.Sut.GetRunAsync(Guid.NewGuid(), default)).Should().BeNull();
    }

    [Fact]
    public async Task GetRunsAndStages_MapRepositoryResults()
    {
        var fixture = CreateFixture();
        var documentId = Guid.NewGuid();
        var run = new ManualProcessingRun { RunId = Guid.NewGuid(), DocumentId = documentId, RunType = "full" };
        var stage = new ManualProcessingStage { StageId = Guid.NewGuid(), RunId = run.RunId, StageName = "parse" };
        fixture.Repository.Setup(repository => repository.GetRunsForDocumentAsync(documentId, It.IsAny<CancellationToken>())).ReturnsAsync([run]);
        fixture.Repository.Setup(repository => repository.GetStagesForRunAsync(run.RunId, It.IsAny<CancellationToken>())).ReturnsAsync([stage]);

        var runs = (await fixture.Sut.GetRunsForDocumentAsync(documentId, default)).ToArray();
        var stages = (await fixture.Sut.GetStagesForRunAsync(run.RunId, default)).ToArray();

        runs.Should().ContainSingle(value => value.RunId == run.RunId);
        stages.Should().ContainSingle(value => value.StageId == stage.StageId);
    }

    [Fact]
    public async Task CreateRunAndReportStart_UpdateDocumentRunAndStage()
    {
        var fixture = CreateFixture();
        var document = CreateDocument();
        var run = new ManualProcessingRun { RunId = Guid.NewGuid(), DocumentId = document.DocumentId };
        fixture.Repository.Setup(repository => repository.CreateRunAsync(It.IsAny<ManualProcessingRun>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManualProcessingRun value, CancellationToken _) => value);
        fixture.Repository.Setup(repository => repository.GetDocumentByIdAsync(document.DocumentId, It.IsAny<CancellationToken>())).ReturnsAsync(document);
        fixture.Repository.Setup(repository => repository.GetRunByIdAsync(run.RunId, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        fixture.Repository.Setup(repository => repository.CreateStageAsync(It.IsAny<ManualProcessingStage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManualProcessingStage value, CancellationToken _) => value);

        var created = await fixture.Sut.CreateRunAsync(new CreateManualRunRequest(document.DocumentId, "full", "parse", "/work"), default);
        await fixture.Sut.ReportStageStartAsync(run.RunId, "chunk", new ManualStageStartRequest("{}"), default);

        created.StartedFromStage.Should().Be("parse");
        document.CurrentStatus.Should().Be(DomainDocumentStatus.Processing);
        document.CurrentStage.Should().Be("chunk");
        run.Status.Should().Be(DomainRunStatus.InProgress);
        fixture.Repository.Verify(repository => repository.UpdateDocumentAsync(document, default), Times.Exactly(2));
        fixture.Repository.Verify(repository => repository.UpdateRunAsync(run, default), Times.Once);
    }

    [Fact]
    public async Task ReportStageComplete_ForCompletion_UpdatesStageRunAndDocument()
    {
        var fixture = CreateFixture();
        var document = CreateDocument();
        var run = new ManualProcessingRun { RunId = Guid.NewGuid(), DocumentId = document.DocumentId };
        var stage = new ManualProcessingStage { RunId = run.RunId, StageName = ManualIngestionStages.Complete, Status = DomainStageStatus.Started };
        fixture.Repository.Setup(repository => repository.GetStagesForRunAsync(run.RunId, It.IsAny<CancellationToken>())).ReturnsAsync([stage]);
        fixture.Repository.Setup(repository => repository.GetRunByIdAsync(run.RunId, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        fixture.Repository.Setup(repository => repository.GetDocumentByIdAsync(document.DocumentId, It.IsAny<CancellationToken>())).ReturnsAsync(document);

        await fixture.Sut.ReportStageCompleteAsync(run.RunId, ManualIngestionStages.Complete, new ManualStageCompleteRequest("artifact", "hash", "{}"), default);

        stage.Status.Should().Be(DomainStageStatus.Completed);
        run.Status.Should().Be(DomainRunStatus.Succeeded);
        document.CurrentStatus.Should().Be(DomainDocumentStatus.Processed);
        document.LastSuccessfulRunId.Should().Be(run.RunId);
        fixture.Repository.Verify(repository => repository.UpdateStageAsync(stage, default), Times.Once);
    }

    [Fact]
    public async Task ReportStageComplete_WhenStageOrRunIsMissing_DoesNotUpdateAnything()
    {
        var fixture = CreateFixture();
        fixture.Repository.Setup(repository => repository.GetStagesForRunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<ManualProcessingStage>());
        fixture.Repository.Setup(repository => repository.GetRunByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((ManualProcessingRun?)null);

        await fixture.Sut.ReportStageCompleteAsync(Guid.NewGuid(), ManualIngestionStages.Complete, new ManualStageCompleteRequest(null, null, null), default);

        fixture.Repository.Verify(repository => repository.UpdateStageAsync(It.IsAny<ManualProcessingStage>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Repository.Verify(repository => repository.UpdateRunAsync(It.IsAny<ManualProcessingRun>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReportStageFail_UpdatesExistingStageRunAndDocument()
    {
        var fixture = CreateFixture();
        var document = CreateDocument();
        var run = new ManualProcessingRun { RunId = Guid.NewGuid(), DocumentId = document.DocumentId };
        var stage = new ManualProcessingStage { RunId = run.RunId, StageName = "parse", Status = DomainStageStatus.Started };
        fixture.Repository.Setup(repository => repository.GetStagesForRunAsync(run.RunId, It.IsAny<CancellationToken>())).ReturnsAsync([stage]);
        fixture.Repository.Setup(repository => repository.GetRunByIdAsync(run.RunId, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        fixture.Repository.Setup(repository => repository.GetDocumentByIdAsync(document.DocumentId, It.IsAny<CancellationToken>())).ReturnsAsync(document);

        await fixture.Sut.ReportStageFailAsync(run.RunId, "parse", new ManualStageFailRequest("bad input", "{}"), default);

        stage.Status.Should().Be(DomainStageStatus.Failed);
        run.Status.Should().Be(DomainRunStatus.Failed);
        document.CurrentStatus.Should().Be(DomainDocumentStatus.Failed);
        document.LastFailure.Should().Be("bad input");
    }

    [Theory]
    [InlineData(ManualIngestionArtifactTypes.Chunks)]
    [InlineData(ManualIngestionArtifactTypes.Entities)]
    [InlineData(ManualIngestionArtifactTypes.Relationships)]
    [InlineData(ManualIngestionArtifactTypes.Vectors)]
    public async Task RegisterArtifact_MapsCountsForEveryKnownArtifactType(string artifactType)
    {
        var fixture = CreateFixture();
        var run = new ManualProcessingRun { RunId = Guid.NewGuid() };
        fixture.Repository.Setup(repository => repository.GetRunByIdAsync(run.RunId, It.IsAny<CancellationToken>())).ReturnsAsync(run);

        await fixture.Sut.RegisterArtifactAsync(run.RunId, new ManualArtifactRegistrationRequest(artifactType, "path", null, null, 7), default);

        new[] { run.ChunkCount, run.GraphEntityCount, run.GraphRelationCount, run.VectorCount }.Should().Contain(7);
        fixture.Repository.Verify(repository => repository.UpdateRunAsync(run, default), Times.Once);
    }

    [Fact]
    public async Task CreateGraphSeedJobAndGetOperations_DelegateAndOrderResults()
    {
        var fixture = CreateFixture();
        var manual = CreateDocument();
        var manualRun = new ManualProcessingRun { RunId = Guid.NewGuid(), DocumentId = manual.DocumentId, StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-2) };
        var job = new IngestionJob { IngestionJobId = Guid.NewGuid(), InputType = IngestionJobType.BikeGraph, Status = IngestionJobStatus.Completed, InputRef = "graph.csv", CreatedAtUtc = DateTimeOffset.UtcNow };
        fixture.Repository.Setup(repository => repository.GetRecentManualOperationsAsync(50, It.IsAny<CancellationToken>())).ReturnsAsync([(manual, manualRun)]);
        fixture.JobRepository.Setup(repository => repository.GetRecentAsync(50, It.IsAny<CancellationToken>())).ReturnsAsync([job]);
        fixture.IngestionJobs.Setup(service => service.StartJobAsync(It.IsAny<IngestionJobStartRequest>(), "user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestionJobStatusResponse());

        await fixture.Sut.CreateGraphSeedJobAsync(new CreateGraphSeedJobRequest("graph.csv", "hash"), "user", default);
        var operations = (await fixture.Sut.GetOperationsAsync(default)).ToArray();

        operations.Select(operation => operation.OperationType).Should().Equal("GraphSeeding", "ManualIngestion");
        fixture.IngestionJobs.Verify(service => service.StartJobAsync(It.Is<IngestionJobStartRequest>(request => request.UploadId == "graph.csv" && request.DocumentType == "bike-graph"), "user", default), Times.Once);
    }

    private static Fixture CreateFixture()
    {
        var repository = new Mock<IManualDocumentRepository>(MockBehavior.Loose);
        var blobStorage = new Mock<IBlobStorageService>(MockBehavior.Loose);
        var ingestionJobs = new Mock<IIngestionJobService>(MockBehavior.Loose);
        var jobRepository = new Mock<IIngestionJobRepository>(MockBehavior.Loose);
        return new Fixture(
            new ManualIngestionService(repository.Object, blobStorage.Object, ingestionJobs.Object, jobRepository.Object, NullLogger<ManualIngestionService>.Instance),
            repository, blobStorage, ingestionJobs, jobRepository);
    }

    private static ManualDocument CreateDocument() => new()
    {
        DocumentId = Guid.NewGuid(),
        SourceFileName = "manual.pdf",
        CanonicalBlobPath = "path",
        DocumentType = "service",
    };

    private sealed record Fixture(
        ManualIngestionService Sut,
        Mock<IManualDocumentRepository> Repository,
        Mock<IBlobStorageService> BlobStorage,
        Mock<IIngestionJobService> IngestionJobs,
        Mock<IIngestionJobRepository> JobRepository);
}
