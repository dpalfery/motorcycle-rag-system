using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;
using MotorcycleRAG.Persistence.Sql;
using MotorcycleRAG.Persistence.Sql.Repositories;
using ManualProcessingRun = MotorcycleRAG.Contracts.Models.DTOs.ManualIngestion.ManualRunDto;
using ManualProcessingStage = MotorcycleRAG.Contracts.Models.DTOs.ManualIngestion.ManualStageDto;
using ManualRunStatus = MotorcycleRAG.Contracts.Models.DTOs.ManualIngestion.ManualRunStatus;
using ManualStageStatus = MotorcycleRAG.Contracts.Models.DTOs.ManualIngestion.ManualStageStatus;

namespace MotorcycleRAG.UnitTests.Persistence.Sql.Repositories;

public sealed class ManualDocumentRepositoryTests
{
    // ── CreateDocumentAsync ──────────────────────────────────────────────

    [Fact]
    public async Task CreateDocumentAsync_ShouldReturnMappedDocument()
    {
        var document = CreateDocument();
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            RepositoryTestReader.CreateTypedReader(CreateDocumentRow(document)),
            command =>
            {
                command.CommandText.Should().Contain("INSERT INTO [dbo].[ManualDocuments]");
                command.Parameters["DocumentId"].Should().Be(document.DocumentId);
                command.Parameters["SourceFileName"].Should().Be(document.SourceFileName);
                command.Parameters["DocumentType"].Should().Be(document.DocumentType);
            });
        var sut = CreateSut(connection);

        var result = await sut.CreateDocumentAsync(document);

        result.Should().NotBeNull();
        result.DocumentId.Should().Be(document.DocumentId);
        result.SourceFileName.Should().Be(document.SourceFileName);
        result.CurrentStatus.Should().Be(document.CurrentStatus);
    }

    [Fact]
    public async Task CreateDocumentAsync_ShouldPropagateConnectionFailure()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.CreateDocumentAsync(CreateDocument());

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Should().BeSameAs(expected);
    }

    // ── GetDocumentByIdAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetDocumentByIdAsync_ShouldReturnMappedDocument_WhenFound()
    {
        var document = CreateDocument();
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            RepositoryTestReader.CreateTypedReader(CreateDocumentRow(document)),
            command =>
            {
                command.CommandText.Should().Contain("WHERE [DocumentId] = @DocumentId");
                command.Parameters["DocumentId"].Should().Be(document.DocumentId);
            });
        var sut = CreateSut(connection);

        var result = await sut.GetDocumentByIdAsync(document.DocumentId);

        result.Should().NotBeNull();
        result!.DocumentId.Should().Be(document.DocumentId);
        result.Make.Should().Be(document.Make);
        result.Model.Should().Be(document.Model);
    }

    [Fact]
    public async Task GetDocumentByIdAsync_ShouldReturnNull_WhenNoRowExists()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(RepositoryTestReader.CreateReaderWithSchema("DocumentId"));
        var sut = CreateSut(connection);

        var result = await sut.GetDocumentByIdAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetDocumentByIdAsync_ShouldPropagateConnectionFailure()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetDocumentByIdAsync(Guid.NewGuid());

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Should().BeSameAs(expected);
    }

    // ── GetAllDocumentsAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetAllDocumentsAsync_ShouldReturnAllMappedDocuments()
    {
        var first = CreateDocument();
        var second = CreateDocument();
        var connection = new FakeDbConnection();
        connection.EnqueueReader(RepositoryTestReader.CreateTypedReader(CreateDocumentRow(first), CreateDocumentRow(second)));
        var sut = CreateSut(connection);

        var result = (await sut.GetAllDocumentsAsync()).ToArray();

        result.Should().HaveCount(2);
        result.Select(d => d.DocumentId).Should().Contain([first.DocumentId, second.DocumentId]);
    }

    [Fact]
    public async Task GetAllDocumentsAsync_ShouldReturnEmpty_WhenNoRowsExist()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(RepositoryTestReader.CreateReaderWithSchema("DocumentId"));
        var sut = CreateSut(connection);

        var result = await sut.GetAllDocumentsAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllDocumentsAsync_ShouldPropagateConnectionFailure()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetAllDocumentsAsync();

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Should().BeSameAs(expected);
    }

    // ── UpdateDocumentAsync ──────────────────────────────────────────────

    [Fact]
    public async Task UpdateDocumentAsync_ShouldExecuteUpdate_WithExpectedParameters()
    {
        var document = CreateDocument();
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(
            1,
            command =>
            {
                command.CommandText.Should().Contain("UPDATE [dbo].[ManualDocuments]");
                command.Parameters["DocumentId"].Should().Be(document.DocumentId);
                command.Parameters["CurrentStatus"].Should().Be((int)ManualDocumentStatus.Processed);
            });
        var sut = CreateSut(connection);

        await sut.UpdateDocumentAsync(document);

        connection.ExecutedCommands.Should().ContainSingle();
    }

    [Fact]
    public async Task UpdateDocumentAsync_ShouldPropagateConnectionFailure()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.UpdateDocumentAsync(CreateDocument());

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Should().BeSameAs(expected);
    }

    // ── CreateRunAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task CreateRunAsync_ShouldReturnMappedRun()
    {
        var run = CreateRun();
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            RepositoryTestReader.CreateTypedReader(CreateRunRow(run)),
            command =>
            {
                command.CommandText.Should().Contain("INSERT INTO [dbo].[ManualProcessingRuns]");
                command.Parameters["RunId"].Should().Be(run.RunId);
                command.Parameters["DocumentId"].Should().Be(run.DocumentId);
                command.Parameters["RunType"].Should().Be(run.RunType);
            });
        var sut = CreateSut(connection);

        var result = await sut.CreateRunAsync(run);

        result.Should().NotBeNull();
        result.RunId.Should().Be(run.RunId);
        result.Status.Should().Be(run.Status);
    }

    [Fact]
    public async Task CreateRunAsync_ShouldPropagateConnectionFailure()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.CreateRunAsync(CreateRun());

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Should().BeSameAs(expected);
    }

    // ── GetRunByIdAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetRunByIdAsync_ShouldReturnMappedRun_WhenFound()
    {
        var run = CreateRun();
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            RepositoryTestReader.CreateTypedReader(CreateRunRow(run)),
            command => command.Parameters["RunId"].Should().Be(run.RunId));
        var sut = CreateSut(connection);

        var result = await sut.GetRunByIdAsync(run.RunId);

        result.Should().NotBeNull();
        result!.RunId.Should().Be(run.RunId);
    }

    [Fact]
    public async Task GetRunByIdAsync_ShouldReturnNull_WhenNoRowExists()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(RepositoryTestReader.CreateReaderWithSchema("RunId"));
        var sut = CreateSut(connection);

        var result = await sut.GetRunByIdAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetRunByIdAsync_ShouldPropagateConnectionFailure()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetRunByIdAsync(Guid.NewGuid());

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Should().BeSameAs(expected);
    }

    // ── GetRunsForDocumentAsync ──────────────────────────────────────────

    [Fact]
    public async Task GetRunsForDocumentAsync_ShouldReturnAllMappedRuns()
    {
        var documentId = Guid.NewGuid();
        var first = CreateRun(documentId);
        var second = CreateRun(documentId);
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            RepositoryTestReader.CreateTypedReader(CreateRunRow(first), CreateRunRow(second)),
            command => command.Parameters["DocumentId"].Should().Be(documentId));
        var sut = CreateSut(connection);

        var result = (await sut.GetRunsForDocumentAsync(documentId)).ToArray();

        result.Should().HaveCount(2);
        result.Select(r => r.RunId).Should().Contain([first.RunId, second.RunId]);
    }

    [Fact]
    public async Task GetRunsForDocumentAsync_ShouldReturnEmpty_WhenNoRowsExist()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(RepositoryTestReader.CreateReaderWithSchema("RunId"));
        var sut = CreateSut(connection);

        var result = await sut.GetRunsForDocumentAsync(Guid.NewGuid());

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRunsForDocumentAsync_ShouldPropagateConnectionFailure()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetRunsForDocumentAsync(Guid.NewGuid());

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Should().BeSameAs(expected);
    }

    // ── UpdateRunAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task UpdateRunAsync_ShouldExecuteUpdate_WithExpectedParameters()
    {
        var run = CreateRun();
        run.Status = ManualRunStatus.Succeeded;
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(
            1,
            command =>
            {
                command.CommandText.Should().Contain("UPDATE [dbo].[ManualProcessingRuns]");
                command.Parameters["RunId"].Should().Be(run.RunId);
                command.Parameters["Status"].Should().Be((int)ManualRunStatus.Succeeded);
            });
        var sut = CreateSut(connection);

        await sut.UpdateRunAsync(run);

        connection.ExecutedCommands.Should().ContainSingle();
    }

    [Fact]
    public async Task UpdateRunAsync_ShouldPropagateConnectionFailure()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.UpdateRunAsync(CreateRun());

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Should().BeSameAs(expected);
    }

    // ── CreateStageAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task CreateStageAsync_ShouldReturnMappedStage()
    {
        var stage = CreateStage();
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            RepositoryTestReader.CreateTypedReader(CreateStageRow(stage)),
            command =>
            {
                command.CommandText.Should().Contain("INSERT INTO [dbo].[ManualProcessingStages]");
                command.Parameters["StageId"].Should().Be(stage.StageId);
                command.Parameters["RunId"].Should().Be(stage.RunId);
                command.Parameters["StageName"].Should().Be(stage.StageName);
            });
        var sut = CreateSut(connection);

        var result = await sut.CreateStageAsync(stage);

        result.Should().NotBeNull();
        result.StageId.Should().Be(stage.StageId);
        result.Status.Should().Be(stage.Status);
    }

    [Fact]
    public async Task CreateStageAsync_ShouldPropagateConnectionFailure()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.CreateStageAsync(CreateStage());

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Should().BeSameAs(expected);
    }

    // ── GetStagesForRunAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetStagesForRunAsync_ShouldReturnAllMappedStages()
    {
        var runId = Guid.NewGuid();
        var first = CreateStage(runId);
        var second = CreateStage(runId);
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            RepositoryTestReader.CreateTypedReader(CreateStageRow(first), CreateStageRow(second)),
            command => command.Parameters["RunId"].Should().Be(runId));
        var sut = CreateSut(connection);

        var result = (await sut.GetStagesForRunAsync(runId)).ToArray();

        result.Should().HaveCount(2);
        result.Select(s => s.StageId).Should().Contain([first.StageId, second.StageId]);
    }

    [Fact]
    public async Task GetStagesForRunAsync_ShouldReturnEmpty_WhenNoRowsExist()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(RepositoryTestReader.CreateReaderWithSchema("StageId"));
        var sut = CreateSut(connection);

        var result = await sut.GetStagesForRunAsync(Guid.NewGuid());

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetStagesForRunAsync_ShouldPropagateConnectionFailure()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetStagesForRunAsync(Guid.NewGuid());

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Should().BeSameAs(expected);
    }

    // ── UpdateStageAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task UpdateStageAsync_ShouldExecuteUpdate_WithExpectedParameters()
    {
        var stage = CreateStage();
        stage.Status = ManualStageStatus.Completed;
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(
            1,
            command =>
            {
                command.CommandText.Should().Contain("UPDATE [dbo].[ManualProcessingStages]");
                command.Parameters["StageId"].Should().Be(stage.StageId);
                command.Parameters["Status"].Should().Be((int)ManualStageStatus.Completed);
            });
        var sut = CreateSut(connection);

        await sut.UpdateStageAsync(stage);

        connection.ExecutedCommands.Should().ContainSingle();
    }

    [Fact]
    public async Task UpdateStageAsync_ShouldPropagateConnectionFailure()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.UpdateStageAsync(CreateStage());

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Should().BeSameAs(expected);
    }

    // ── GetRecentManualOperationsAsync ───────────────────────────────────

    [Fact]
    public async Task GetRecentManualOperationsAsync_ShouldReturnMappedDocumentRunPairs()
    {
        var document = CreateDocument();
        var run = CreateRun(document.DocumentId);
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            CreateMultiMapReader((document, run)),
            command => command.Parameters["Top"].Should().Be(5));
        var sut = CreateSut(connection);

        var result = (await sut.GetRecentManualOperationsAsync(5)).ToArray();

        result.Should().ContainSingle();
        result[0].Document.DocumentId.Should().Be(document.DocumentId);
        result[0].Document.SourceFileName.Should().Be(document.SourceFileName);
        result[0].Run.RunId.Should().Be(run.RunId);
        result[0].Run.DocumentId.Should().Be(document.DocumentId);
        result[0].Run.Status.Should().Be(run.Status);
    }

    [Fact]
    public async Task GetRecentManualOperationsAsync_ShouldReturnMultiplePairs()
    {
        var firstDocument = CreateDocument();
        var firstRun = CreateRun(firstDocument.DocumentId);
        var secondDocument = CreateDocument();
        var secondRun = CreateRun(secondDocument.DocumentId);
        var connection = new FakeDbConnection();
        connection.EnqueueReader(CreateMultiMapReader((firstDocument, firstRun), (secondDocument, secondRun)));
        var sut = CreateSut(connection);

        var result = (await sut.GetRecentManualOperationsAsync(10)).ToArray();

        result.Should().HaveCount(2);
        result.Select(pair => pair.Run.RunId).Should().Contain([firstRun.RunId, secondRun.RunId]);
    }

    [Fact]
    public async Task GetRecentManualOperationsAsync_ShouldReturnEmpty_WhenNoRowsExist()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(CreateMultiMapReader());
        var sut = CreateSut(connection);

        var result = await sut.GetRecentManualOperationsAsync(5);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRecentManualOperationsAsync_ShouldPropagateConnectionFailure()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetRecentManualOperationsAsync(5);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Should().BeSameAs(expected);
    }

    // ── Test helpers ─────────────────────────────────────────────────────

    private static ManualDocumentRepository CreateSut(FakeDbConnection? connection = null)
    {
        var factory = new Mock<ISqlConnectionFactory>();
        factory
            .Setup(x => x.CreateOpenConnectionAsync())
            .ReturnsAsync(connection ?? new FakeDbConnection());
        return new ManualDocumentRepository(factory.Object, NullLogger<ManualDocumentRepository>.Instance);
    }

    private static ManualDocumentRepository CreateThrowingSut(Exception exception)
    {
        var factory = new Mock<ISqlConnectionFactory>();
        factory.Setup(x => x.CreateOpenConnectionAsync()).ThrowsAsync(exception);
        return new ManualDocumentRepository(factory.Object, NullLogger<ManualDocumentRepository>.Instance);
    }

    private static ManualDocument CreateDocument()
    {
        // CanonicalizedAtUtc/CurrentStatus/CurrentStage/LastProcessedAtUtc/LastSuccessfulRunId
        // are private-set and only reachable through the entity's named transitions.
        var document = new ManualDocument
        {
            DocumentId = Guid.NewGuid(),
            SourceFileName = "honda-cbr.pdf",
            CanonicalBlobContainer = "manuals",
            CanonicalBlobPath = "canonical/honda-cbr.pdf",
            CanonicalBlobUri = "https://blob.example.com/manuals/honda-cbr.pdf",
            SourceContentHash = "hash-1",
            DocumentType = "ServiceManual",
            Make = "Honda",
            Model = "CBR600RR",
            Year = 2024,
            UploadedAtUtc = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero),
        };
        document.MarkCanonicalized(new DateTimeOffset(2026, 7, 1, 9, 5, 0, TimeSpan.Zero));
        document.BeginProcessing("indexed");
        document.MarkProcessed(Guid.NewGuid(), new DateTimeOffset(2026, 7, 1, 9, 10, 0, TimeSpan.Zero));
        return document;
    }

    private static ManualProcessingRun CreateRun(Guid? documentId = null) => new()
    {
        RunId = Guid.NewGuid(),
        DocumentId = documentId ?? Guid.NewGuid(),
        RunType = "Full",
        StartedAtUtc = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero),
        CompletedAtUtc = new DateTimeOffset(2026, 7, 1, 9, 15, 0, TimeSpan.Zero),
        Status = ManualRunStatus.Succeeded,
        StartedFromStage = "canonicalize",
        CompletedStage = "index",
        LocalWorkingFolder = "/tmp/run",
        ProcessorHost = "worker-1",
        ErrorSummary = null,
        ChunkCount = 12,
        GraphEntityCount = 4,
        GraphRelationCount = 2,
        VectorCount = 12
    };

    private static ManualProcessingStage CreateStage(Guid? runId = null) => new()
    {
        StageId = Guid.NewGuid(),
        RunId = runId ?? Guid.NewGuid(),
        StageName = "chunk",
        Status = ManualStageStatus.Started,
        StartedAtUtc = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero),
        CompletedAtUtc = null,
        ArtifactPath = "artifacts/chunk.json",
        ArtifactHash = "hash-stage",
        MetadataJson = """{"pages":10}""",
        ErrorDetail = null
    };

    private static Dictionary<string, object?> CreateDocumentRow(ManualDocument document) => new()
    {
        ["DocumentId"] = document.DocumentId,
        ["SourceFileName"] = document.SourceFileName,
        ["CanonicalBlobContainer"] = document.CanonicalBlobContainer,
        ["CanonicalBlobPath"] = document.CanonicalBlobPath,
        ["CanonicalBlobUri"] = document.CanonicalBlobUri,
        ["SourceContentHash"] = document.SourceContentHash,
        ["DocumentType"] = document.DocumentType,
        ["Make"] = document.Make,
        ["Model"] = document.Model,
        ["Year"] = document.Year,
        ["UploadedAtUtc"] = document.UploadedAtUtc,
        ["CanonicalizedAtUtc"] = document.CanonicalizedAtUtc,
        ["LastProcessedAtUtc"] = document.LastProcessedAtUtc,
        ["CurrentStatus"] = document.CurrentStatus.ToString(),
        ["CurrentStage"] = document.CurrentStage,
        ["LastSuccessfulRunId"] = document.LastSuccessfulRunId,
        ["LastFailure"] = document.LastFailure
    };

    private static Dictionary<string, object?> CreateRunRow(ManualProcessingRun run) => new()
    {
        ["RunId"] = run.RunId,
        ["DocumentId"] = run.DocumentId,
        ["RunType"] = run.RunType,
        ["StartedAtUtc"] = run.StartedAtUtc,
        ["CompletedAtUtc"] = run.CompletedAtUtc,
        ["Status"] = run.Status.ToString(),
        ["StartedFromStage"] = run.StartedFromStage,
        ["CompletedStage"] = run.CompletedStage,
        ["LocalWorkingFolder"] = run.LocalWorkingFolder,
        ["ProcessorHost"] = run.ProcessorHost,
        ["ErrorSummary"] = run.ErrorSummary,
        ["ChunkCount"] = run.ChunkCount,
        ["GraphEntityCount"] = run.GraphEntityCount,
        ["GraphRelationCount"] = run.GraphRelationCount,
        ["VectorCount"] = run.VectorCount
    };

    private static Dictionary<string, object?> CreateStageRow(ManualProcessingStage stage) => new()
    {
        ["StageId"] = stage.StageId,
        ["RunId"] = stage.RunId,
        ["StageName"] = stage.StageName,
        ["Status"] = stage.Status.ToString(),
        ["StartedAtUtc"] = stage.StartedAtUtc,
        ["CompletedAtUtc"] = stage.CompletedAtUtc,
        ["ArtifactPath"] = stage.ArtifactPath,
        ["ArtifactHash"] = stage.ArtifactHash,
        ["MetadataJson"] = stage.MetadataJson,
        ["ErrorDetail"] = stage.ErrorDetail
    };

    /// <summary>
    /// Builds a multi-map <see cref="System.Data.Common.DbDataReader"/> for the
    /// joined document/run projection used by <c>GetRecentManualOperationsAsync</c>.
    /// Dapper's multi-mapping (<c>splitOn: "RunId"</c>) relies on positional column
    /// names that legitimately repeat (both the document and the run carry a
    /// <c>DocumentId</c> column). This reader exposes duplicate column names and
    /// reports each column's real CLR type from <c>GetFieldType</c>.
    /// </summary>
    private static DbDataReader CreateMultiMapReader(params (ManualDocument Document, ManualProcessingRun Run)[] pairs)
    {
        string[] columnNames =
        [
            "DocumentId", "SourceFileName", "CanonicalBlobContainer", "CanonicalBlobPath", "CanonicalBlobUri",
            "SourceContentHash", "DocumentType", "Make", "Model", "Year", "UploadedAtUtc", "CanonicalizedAtUtc",
            "LastProcessedAtUtc", "CurrentStatus", "CurrentStage", "LastSuccessfulRunId", "LastFailure",
            "RunId", "DocumentId", "RunType", "StartedAtUtc", "CompletedAtUtc", "Status", "StartedFromStage",
            "CompletedStage", "LocalWorkingFolder", "ProcessorHost", "ErrorSummary", "ChunkCount",
            "GraphEntityCount", "GraphRelationCount", "VectorCount"
        ];

        Type[] columnTypes =
        [
            typeof(Guid), typeof(string), typeof(string), typeof(string), typeof(string),
            typeof(string), typeof(string), typeof(string), typeof(string), typeof(int), typeof(DateTimeOffset), typeof(DateTimeOffset),
            typeof(DateTimeOffset), typeof(string), typeof(string), typeof(Guid), typeof(string),
            typeof(Guid), typeof(Guid), typeof(string), typeof(DateTimeOffset), typeof(DateTimeOffset), typeof(string), typeof(string),
            typeof(string), typeof(string), typeof(string), typeof(string), typeof(int),
            typeof(int), typeof(int), typeof(int)
        ];

        var rows = pairs
            .Select(pair => new object?[]
            {
                pair.Document.DocumentId, pair.Document.SourceFileName, pair.Document.CanonicalBlobContainer,
                pair.Document.CanonicalBlobPath, pair.Document.CanonicalBlobUri, pair.Document.SourceContentHash,
                pair.Document.DocumentType, pair.Document.Make, pair.Document.Model, pair.Document.Year,
                pair.Document.UploadedAtUtc, pair.Document.CanonicalizedAtUtc, pair.Document.LastProcessedAtUtc,
                pair.Document.CurrentStatus.ToString(), pair.Document.CurrentStage, pair.Document.LastSuccessfulRunId,
                pair.Document.LastFailure,
                pair.Run.RunId, pair.Run.DocumentId, pair.Run.RunType, pair.Run.StartedAtUtc,
                pair.Run.CompletedAtUtc, pair.Run.Status.ToString(), pair.Run.StartedFromStage,
                pair.Run.CompletedStage, pair.Run.LocalWorkingFolder, pair.Run.ProcessorHost,
                pair.Run.ErrorSummary, pair.Run.ChunkCount, pair.Run.GraphEntityCount,
                pair.Run.GraphRelationCount, pair.Run.VectorCount
            })
            .ToList();

        return RepositoryTestReader.CreateMultiMapReader(columnNames, columnTypes, rows);
    }
}
