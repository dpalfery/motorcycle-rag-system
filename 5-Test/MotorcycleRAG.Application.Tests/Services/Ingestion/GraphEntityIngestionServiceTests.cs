using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MotorcycleRAG.Application.Services.Ingestion;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Repositories;
using MotorcycleRAG.Domain.Entities;

namespace MotorcycleRAG.UnitTests.Services.Ingestion;

public sealed class GraphEntityIngestionServiceTests
{
    private const string BlobContainer = "raw-uploads";

    [Fact]
    public void Constructor_WithNullDependencies_ThrowsArgumentNullException()
    {
        var blobs = new Mock<IBlobStorageService>();
        var graphs = new Mock<IGraphRepository>();

        ((Action)(() => new GraphEntityIngestionService(null!, graphs.Object, NullLogger<GraphEntityIngestionService>.Instance)))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => new GraphEntityIngestionService(blobs.Object, null!, NullLogger<GraphEntityIngestionService>.Instance)))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => new GraphEntityIngestionService(blobs.Object, graphs.Object, null!)))
            .Should().Throw<ArgumentNullException>();
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
            It.IsAny<IReadOnlyList<GraphNode>>(), It.IsAny<CancellationToken>()), Times.Never);
        graphs.Verify(service => service.UpsertEdgesAsync(
            It.IsAny<IReadOnlyList<GraphEdge>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task IngestAsync_WithNoNodesOrEdges_DoesNotCallGraphRepository()
    {
        var sut = CreateSut(out var blobs, out var graphs);
        SetExistingJson(blobs, "upload-empty", "[{\"nodes\":[],\"edges\":[]}]");

        await sut.IngestAsync("upload-empty");

        graphs.Verify(service => service.UpsertNodesAsync(
            It.IsAny<IReadOnlyList<GraphNode>>(), It.IsAny<CancellationToken>()), Times.Never);
        graphs.Verify(service => service.UpsertEdgesAsync(
            It.IsAny<IReadOnlyList<GraphEdge>>(), It.IsAny<CancellationToken>()), Times.Never);
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

        IReadOnlyList<GraphNode>? savedNodes = null;
        IReadOnlyList<GraphEdge>? savedEdges = null;
        graphs.Setup(service => service.UpsertNodesAsync(It.IsAny<IReadOnlyList<GraphNode>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<GraphNode>, CancellationToken>((nodes, _) => savedNodes = nodes)
            .Returns(Task.CompletedTask);
        graphs.Setup(service => service.UpsertEdgesAsync(It.IsAny<IReadOnlyList<GraphEdge>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<GraphEdge>, CancellationToken>((edges, _) => savedEdges = edges)
            .Returns(Task.CompletedTask);

        await sut.IngestAsync("upload-3");

        savedNodes.Should().HaveCount(3);
        savedNodes![0].Should().Match<GraphNode>(node =>
            node.Id == firstNodeId && node.Name == "Wheel" && node.Type == "Component" &&
            node.Description == "Front wheel" && node.SourceDocumentId == sourceDocumentId);
        savedNodes[1].Id.Should().NotBe(Guid.Empty);
        savedNodes[1].SourceDocumentId.Should().BeNull();
        savedNodes[2].Id.Should().NotBe(Guid.Empty);
        savedNodes[2].SourceDocumentId.Should().BeNull();

        savedEdges.Should().ContainSingle();
        savedEdges![0].Should().Match<GraphEdge>(edge =>
            edge.FromNodeId == firstNodeId && edge.ToNodeId == secondNodeId &&
            edge.RelationshipType == "PART_OF" && edge.Weight == 0.8 && edge.Context == "wheel assembly");
    }

    private static GraphEntityIngestionService CreateSut(
        out Mock<IBlobStorageService> blobs,
        out Mock<IGraphRepository> graphs)
    {
        blobs = new Mock<IBlobStorageService>(MockBehavior.Strict);
        graphs = new Mock<IGraphRepository>(MockBehavior.Strict);
        return new GraphEntityIngestionService(
            blobs.Object,
            graphs.Object,
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
}
