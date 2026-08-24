using System.Reflection;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MotorcycleRAG.Application.Services.Ingestion;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs.Graph;
using MotorcycleRAG.Contracts.Repositories;
using MotorcycleRAG.Domain.Entities;

namespace MotorcycleRAG.UnitTests.Services.Ingestion;

public sealed class GraphEntityIngestionServiceTests
{
    private const string BlobContainer = "raw-uploads";

    /// <summary>
    /// Plan `2026-08-02-anchor-id-debt-cleanup.md` T1: the legacy 3-parameter constructor that left
    /// <see cref="IManualDocumentRepository"/> null is deleted; the service must expose exactly one public
    /// constructor taking the four production dependencies. A second constructor would re-introduce the
    /// silent no-op path in <see cref="GraphEntityIngestionService.StampSourceContentHashAsync"/> and the
    /// D4 vector&lt;-&gt;graph anchor stamping it gates.
    /// </summary>
    [Fact]
    public void Constructor_DeclaresExactlyOnePublicConstructor_WithFourRequiredDependencies()
    {
        var ctors = typeof(GraphEntityIngestionService).GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        ctors.Should().HaveCount(1, "the legacy 3-parameter constructor must be removed so IManualDocumentRepository is always resolved");

        var ctor = ctors.Single();
        var parameters = ctor.GetParameters();
        parameters.Should().HaveCount(4);
        parameters[0].ParameterType.Should().Be<IBlobStorageService>();
        parameters[1].ParameterType.Should().Be<IGraphRepository>();
        parameters[2].ParameterType.Should().Be<IManualDocumentRepository>();
        parameters[3].ParameterType.Should().Be<ILogger<GraphEntityIngestionService>>();
    }

    [Fact]
    public void Constructor_WithNullDependencies_ThrowsArgumentNullException()
    {
        var blobs = new Mock<IBlobStorageService>();
        var graphs = new Mock<IGraphRepository>();
        var manualDocuments = new Mock<IManualDocumentRepository>();

        Assert.Throws<ArgumentNullException>(() => new GraphEntityIngestionService(null!, graphs.Object, manualDocuments.Object, NullLogger<GraphEntityIngestionService>.Instance));
        Assert.Throws<ArgumentNullException>(() => new GraphEntityIngestionService(blobs.Object, null!, manualDocuments.Object, NullLogger<GraphEntityIngestionService>.Instance));
        Assert.Throws<ArgumentNullException>(() => new GraphEntityIngestionService(blobs.Object, graphs.Object, null!, NullLogger<GraphEntityIngestionService>.Instance));
        Assert.Throws<ArgumentNullException>(() => new GraphEntityIngestionService(blobs.Object, graphs.Object, manualDocuments.Object, null!));
    }

    [Fact]
    public async Task IngestAsync_WithBlankUploadId_ThrowsArgumentException()
    {
        var sut = CreateSut(out _, out _);

        var act = () => sut.IngestAsync(" ");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IngestAsync_WhenGraphEntityBlobIsUnavailable_ReturnsWithoutDownloading(bool existsThrows)
    {
        var sut = CreateSut(out var blobs, out var graphs);
        if (existsThrows)
        {
            blobs.Setup(service => service.ExistsAsync(
                    BlobContainer,
                    "graph-entities/upload-1/entities.json",
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("storage unavailable"));
        }
        else
        {
            blobs.Setup(service => service.ExistsAsync(
                    BlobContainer,
                    "graph-entities/upload-1/entities.json",
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
        }

        await sut.IngestAsync("upload-1");

        blobs.Verify(service => service.DownloadAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        graphs.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("download-failure")]
    [InlineData("empty-document")]
    [InlineData("invalid-json")]
    [InlineData("no-documents")]
    public async Task IngestAsync_WhenGraphEntityPayloadCannotBeUsed_ReturnsWithoutUpserting(string scenario)
    {
        var sut = CreateSut(out var blobs, out var graphs);
        blobs.Setup(service => service.ExistsAsync(
                BlobContainer,
                "graph-entities/upload-2/entities.json",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        if (scenario == "download-failure")
        {
            blobs.Setup(service => service.DownloadAsync(
                    BlobContainer,
                    "graph-entities/upload-2/entities.json",
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("download unavailable"));
        }
        else
        {
            var payload = scenario switch
            {
                "empty-document" => Array.Empty<byte>(),
                "invalid-json" => Encoding.UTF8.GetBytes("not-json"),
                _ => Encoding.UTF8.GetBytes("[]"),
            };
            blobs.Setup(service => service.DownloadAsync(
                    BlobContainer,
                    "graph-entities/upload-2/entities.json",
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MemoryStream(payload));
        }

        await sut.IngestAsync("upload-2");

        graphs.Verify(service => service.UpsertNodesAsync(
            It.IsAny<IReadOnlyList<GraphNodeDto>>(), It.IsAny<CancellationToken>()), Times.Never);
        graphs.Verify(service => service.UpsertEdgesAsync(
            It.IsAny<IReadOnlyList<GraphEdgeDto>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task IngestAsync_WithNoNodesOrEdges_DoesNotCallGraphRepository()
    {
        var sut = CreateSut(out var blobs, out var graphs);
        SetExistingJson(blobs, "upload-empty", "[{\"nodes\":[],\"edges\":[]}]");

        await sut.IngestAsync("upload-empty");

        graphs.Verify(service => service.UpsertNodesAsync(
            It.IsAny<IReadOnlyList<GraphNodeDto>>(), It.IsAny<CancellationToken>()), Times.Never);
        graphs.Verify(service => service.UpsertEdgesAsync(
            It.IsAny<IReadOnlyList<GraphEdgeDto>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task IngestAsync_WithGraphEntities_MapsValidDataAndSkipsInvalidEdges()
    {
        var sourceDocumentId = Guid.NewGuid();
        var firstNodeId = Guid.NewGuid();
        var secondNodeId = Guid.NewGuid();
        var sut = CreateSut(out var blobs, out var graphs);
        SetExistingJson(blobs, "upload-3", $$"""
            [{
              "nodes": [
                { "id": "{{firstNodeId}}", "name": "Wheel", "type": "Component", "description": "Front wheel", "sourceDocumentId": "{{sourceDocumentId}}" },
                { "id": "not-a-guid", "name": "Brake", "type": "Component", "sourceDocumentId": "not-a-guid" },
                { "id": "", "name": "Frame", "type": "Component" }
              ],
              "edges": [
                { "fromNodeId": "{{firstNodeId}}", "toNodeId": "{{secondNodeId}}", "relationshipType": "PART_OF", "weight": 0.8, "context": "wheel assembly" },
                { "fromNodeId": "not-a-guid", "toNodeId": "{{secondNodeId}}", "relationshipType": "IGNORED" },
                { "fromNodeId": "{{firstNodeId}}", "toNodeId": "not-a-guid", "relationshipType": "IGNORED" }
              ]
            }]
            """);

        IReadOnlyList<GraphNodeDto>? savedNodes = null;
        IReadOnlyList<GraphEdgeDto>? savedEdges = null;
        graphs.Setup(service => service.UpsertNodesAsync(It.IsAny<IReadOnlyList<GraphNodeDto>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<GraphNodeDto>, CancellationToken>((nodes, _) => savedNodes = nodes)
            .Returns(Task.CompletedTask);
        graphs.Setup(service => service.UpsertEdgesAsync(It.IsAny<IReadOnlyList<GraphEdgeDto>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<GraphEdgeDto>, CancellationToken>((edges, _) => savedEdges = edges)
            .Returns(Task.CompletedTask);

        await sut.IngestAsync("upload-3");

        savedNodes.Should().HaveCount(3);
        savedNodes![0].Should().Match<GraphNodeDto>(node =>
            node.Id == firstNodeId && node.Name == "Wheel" && node.Type == "Component" &&
            node.Description == "Front wheel" && node.SourceDocumentId == sourceDocumentId);
        savedNodes[1].Id.Should().NotBe(Guid.Empty);
        savedNodes[1].SourceDocumentId.Should().BeNull();
        savedNodes[2].Id.Should().NotBe(Guid.Empty);
        savedNodes[2].SourceDocumentId.Should().BeNull();

        savedEdges.Should().ContainSingle();
        savedEdges![0].Should().Match<GraphEdgeDto>(edge =>
            edge.FromNodeId == firstNodeId && edge.ToNodeId == secondNodeId &&
            edge.RelationshipType == "PART_OF" && edge.Weight == 0.8 && edge.Context == "wheel assembly");
    }

    // -----------------------------------------------------------------------------------------
    // T6 (plan `6-Docs/plans/2026-08-01-vector-graph-anchor-id-contract.md`, D1/D2/D4): narrow
    // Chunk/Document field-shape validation and SOURCED_FROM/PART_OF endpoint-type quarantine.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task IngestAsync_WithChunkTypeNode_MapsChunkIdFieldOntoGraphNodeDto()
    {
        var sut = CreateSutWithAllDependencies(out var blobs, out var graphs, out _, out _);
        var chunkNodeId = Guid.NewGuid();
        SetExistingJson(blobs, "upload-chunk-anchor", $$"""
            [{
              "nodes": [
                { "id": "{{chunkNodeId}}", "name": "Chunk 1", "type": "Chunk", "chunkId": "c-1" }
              ],
              "edges": []
            }]
            """);

        IReadOnlyList<GraphNodeDto>? savedNodes = null;
        graphs.Setup(service => service.UpsertNodesAsync(It.IsAny<IReadOnlyList<GraphNodeDto>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<GraphNodeDto>, CancellationToken>((nodes, _) => savedNodes = nodes)
            .Returns(Task.CompletedTask);

        var act = () => sut.IngestAsync("upload-chunk-anchor");

        await act.Should().NotThrowAsync(
            "parsing/mapping a Chunk-type node must never throw, per the existing no-throw ingestion convention");
        savedNodes.Should().ContainSingle(node => node.Id == chunkNodeId);
        savedNodes!.Single(node => node.Id == chunkNodeId).ChunkId.Should().Be("c-1",
            "a Chunk-type node's JSON \"chunkId\" field is the D1/D4 vector<->graph anchor and must reach GraphNodeDto.ChunkId");
    }

    [Fact]
    public async Task IngestAsync_WithChunkTypeNodeMissingChunkId_QuarantinesNodeWithoutThrowingAndLogsWarning()
    {
        var sut = CreateSutWithAllDependencies(out var blobs, out var graphs, out _, out var logger);
        var validChunkId = Guid.NewGuid();
        var orphanChunkId = Guid.NewGuid();
        SetExistingJson(blobs, "upload-chunk-quarantine", $$"""
            [{
              "nodes": [
                { "id": "{{validChunkId}}", "name": "Valid Chunk", "type": "Chunk", "chunkId": "c-2" },
                { "id": "{{orphanChunkId}}", "name": "Orphan Chunk", "type": "Chunk" }
              ],
              "edges": []
            }]
            """);

        IReadOnlyList<GraphNodeDto>? savedNodes = null;
        graphs.Setup(service => service.UpsertNodesAsync(It.IsAny<IReadOnlyList<GraphNodeDto>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<GraphNodeDto>, CancellationToken>((nodes, _) => savedNodes = nodes)
            .Returns(Task.CompletedTask);

        var act = () => sut.IngestAsync("upload-chunk-quarantine");

        await act.Should().NotThrowAsync(
            "a Chunk-type node missing the field its type requires must be quarantined with a log warning, never an exception");
        savedNodes.Should().NotBeNull();
        savedNodes!.Should().ContainSingle(node => node.Id == validChunkId,
            "the well-formed Chunk node must still be upserted");
        savedNodes.Should().NotContain(node => node.Id == orphanChunkId,
            "a Chunk-type node with no chunkId violates the D2 narrow field-shape rule and must be quarantined, not upserted");
        VerifyWarningLogged(logger, Times.AtLeastOnce());
    }

    [Fact]
    public async Task IngestAsync_StampsManualDocumentSourceContentHash_OnNodesForThatDocument_AndNullNotEmptyWhenDocumentMissing()
    {
        var sut = CreateSutWithAllDependencies(out var blobs, out var graphs, out var manualDocuments, out _);
        var documentWithHashId = Guid.NewGuid();
        var documentWithoutHashId = Guid.NewGuid();
        var nodeAId = Guid.NewGuid();
        var nodeBId = Guid.NewGuid();
        var nodeCId = Guid.NewGuid();
        SetExistingJson(blobs, "upload-hash-stamp", $$"""
            [{
              "nodes": [
                { "id": "{{nodeAId}}", "name": "Spec A", "type": "Spec", "sourceDocumentId": "{{documentWithHashId}}" },
                { "id": "{{nodeBId}}", "name": "Spec B", "type": "Spec", "sourceDocumentId": "{{documentWithHashId}}" },
                { "id": "{{nodeCId}}", "name": "Spec C", "type": "Spec", "sourceDocumentId": "{{documentWithoutHashId}}" }
              ],
              "edges": []
            }]
            """);

        var manualDocumentWithHash = ManualDocument.Create(
            documentWithHashId,
            "manual.pdf",
            "moto-manuals",
            "path/manual.pdf",
            "PDFManual",
            sourceContentHash: "h1");
        manualDocuments
            .Setup(repository => repository.GetDocumentByIdAsync(documentWithHashId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(manualDocumentWithHash);
        manualDocuments
            .Setup(repository => repository.GetDocumentByIdAsync(documentWithoutHashId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManualDocument?)null);

        IReadOnlyList<GraphNodeDto>? savedNodes = null;
        graphs.Setup(service => service.UpsertNodesAsync(It.IsAny<IReadOnlyList<GraphNodeDto>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<GraphNodeDto>, CancellationToken>((nodes, _) => savedNodes = nodes)
            .Returns(Task.CompletedTask);

        var act = () => sut.IngestAsync("upload-hash-stamp");

        await act.Should().NotThrowAsync();
        savedNodes.Should().HaveCount(3);
        savedNodes!.Where(node => node.SourceDocumentId == documentWithHashId).Should()
            .OnlyContain(node => node.SourceContentHash == "h1",
                "D4: every GraphNodeDto belonging to a document with a resolvable ManualDocument.SourceContentHash must carry that same hash");
        var nodeWithoutDocument = savedNodes.Single(node => node.SourceDocumentId == documentWithoutHashId);
        nodeWithoutDocument.SourceContentHash.Should().BeNull(
            "a missing ManualDocument must propagate SourceContentHash as null, never string.Empty " +
            "(cross-task contract shared with T8/T13 — divergence here caused a data-corruption defect elsewhere in this plan)");
    }

    [Fact]
    public async Task IngestAsync_QuarantinesSourcedFromEdgeWithWrongTargetType_ButKeepsValidPartOfEdge()
    {
        var sut = CreateSutWithAllDependencies(out var blobs, out var graphs, out _, out var logger);
        var chunkNodeId = Guid.NewGuid();
        var documentNodeId = Guid.NewGuid();
        var manufacturerNodeId = Guid.NewGuid();
        var specNodeId = Guid.NewGuid();
        SetExistingJson(blobs, "upload-edge-endpoint", $$"""
            [{
              "nodes": [
                { "id": "{{chunkNodeId}}", "name": "Chunk Y", "type": "Chunk", "chunkId": "c-3" },
                { "id": "{{documentNodeId}}", "name": "Document Y", "type": "Document" },
                { "id": "{{manufacturerNodeId}}", "name": "Honda", "type": "Manufacturer" },
                { "id": "{{specNodeId}}", "name": "Spec Y", "type": "Spec" }
              ],
              "edges": [
                { "fromNodeId": "{{chunkNodeId}}", "toNodeId": "{{documentNodeId}}", "relationshipType": "PART_OF", "weight": 1.0, "context": "chunk ordinal 0" },
                { "fromNodeId": "{{specNodeId}}", "toNodeId": "{{manufacturerNodeId}}", "relationshipType": "SOURCED_FROM", "weight": 0.9, "context": "wrong target type" }
              ]
            }]
            """);

        IReadOnlyList<GraphNodeDto>? savedNodes = null;
        IReadOnlyList<GraphEdgeDto>? savedEdges = null;
        graphs.Setup(service => service.UpsertNodesAsync(It.IsAny<IReadOnlyList<GraphNodeDto>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<GraphNodeDto>, CancellationToken>((nodes, _) => savedNodes = nodes)
            .Returns(Task.CompletedTask);
        graphs.Setup(service => service.UpsertEdgesAsync(It.IsAny<IReadOnlyList<GraphEdgeDto>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<GraphEdgeDto>, CancellationToken>((edges, _) => savedEdges = edges)
            .Returns(Task.CompletedTask);

        var act = () => sut.IngestAsync("upload-edge-endpoint");

        await act.Should().NotThrowAsync(
            "an edge whose endpoints violate the ontology's SOURCED_FROM/PART_OF type rule must be quarantined with a log warning, never an exception");
        savedNodes.Should().NotBeNull();
        savedEdges.Should().NotBeNull();
        savedEdges!.Should().ContainSingle(edge =>
                edge.FromNodeId == chunkNodeId && edge.ToNodeId == documentNodeId && edge.RelationshipType == "PART_OF",
            "a PART_OF edge from a Chunk-type node to a Document-type node is valid per the ontology and must be upserted");
        savedEdges.Should().NotContain(edge =>
                edge.RelationshipType == "SOURCED_FROM" && edge.ToNodeId == manufacturerNodeId,
            "SOURCED_FROM must target a Chunk node per the ontology; a Manufacturer target is a defect and must be quarantined");
        VerifyWarningLogged(logger, Times.AtLeastOnce());
    }

    private static GraphEntityIngestionService CreateSut(
        out Mock<IBlobStorageService> blobs,
        out Mock<IGraphRepository> graphs)
    {
        blobs = new Mock<IBlobStorageService>(MockBehavior.Strict);
        graphs = new Mock<IGraphRepository>(MockBehavior.Strict);
        var manualDocuments = new Mock<IManualDocumentRepository>(MockBehavior.Strict);
        return new GraphEntityIngestionService(
            blobs.Object,
            graphs.Object,
            manualDocuments.Object,
            NullLogger<GraphEntityIngestionService>.Instance);
    }

    private static void SetExistingJson(Mock<IBlobStorageService> blobs, string uploadId, string json)
    {
        var blobPath = $"graph-entities/{uploadId}/entities.json";
        blobs.Setup(service => service.ExistsAsync(BlobContainer, blobPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        blobs.Setup(service => service.DownloadAsync(BlobContainer, blobPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(Encoding.UTF8.GetBytes(json)));
    }

    /// <summary>
    /// Constructs the SUT with all four dependencies wired through captures for assertions. Plan T1
    /// (`6-Docs/plans/2026-08-02-anchor-id-debt-cleanup.md`) removed the legacy 3-parameter constructor,
    /// so the 4-parameter production constructor is now the only one — no reflection resolution needed.
    /// </summary>
    private static GraphEntityIngestionService CreateSutWithAllDependencies(
        out Mock<IBlobStorageService> blobs,
        out Mock<IGraphRepository> graphs,
        out Mock<IManualDocumentRepository> manualDocuments,
        out Mock<ILogger<GraphEntityIngestionService>> logger)
    {
        blobs = new Mock<IBlobStorageService>(MockBehavior.Strict);
        graphs = new Mock<IGraphRepository>(MockBehavior.Strict);
        manualDocuments = new Mock<IManualDocumentRepository>(MockBehavior.Strict);
        logger = new Mock<ILogger<GraphEntityIngestionService>>();

        return new GraphEntityIngestionService(
            blobs.Object,
            graphs.Object,
            manualDocuments.Object,
            logger.Object);
    }

    private static void VerifyWarningLogged(Mock<ILogger<GraphEntityIngestionService>> logger, Times times) =>
        logger.Verify(
            entry => entry.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);
}
