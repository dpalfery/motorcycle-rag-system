using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.Domian.Tests.Domain;

public sealed class GraphAndIndexModelTests
{
    [Fact]
    public void GraphNode_WhenCreated_InitializesIdentityAndOptionalState()
    {
        // Arrange
        var node = new GraphNode();

        // Act
        var state = new { node.Id, node.Name, node.Type, node.Description, node.SourceDocumentId, node.CreatedAtUtc, node.UpdatedAtUtc };

        // Assert
        state.Id.Should().NotBe(Guid.Empty);
        state.Name.Should().BeEmpty();
        state.Type.Should().BeEmpty();
        state.Description.Should().BeNull();
        state.SourceDocumentId.Should().BeNull();
        state.CreatedAtUtc.Should().BeAfter(DateTimeOffset.UnixEpoch);
        state.UpdatedAtUtc.Should().BeNull();
    }

    [Fact]
    public void GraphEdge_WhenConfigured_PreservesDirectedRelationshipAndContext()
    {
        // Arrange
        var fromNodeId = Guid.NewGuid();
        var toNodeId = Guid.NewGuid();
        var createdAt = DateTimeOffset.Parse("2026-07-12T12:00:00+00:00");

        // Act
        var edge = new GraphEdge
        {
            FromNodeId = fromNodeId,
            ToNodeId = toNodeId,
            RelationshipType = "REQUIRES",
            Weight = 0.85,
            Context = "The manual requires a torque check.",
            CreatedAtUtc = createdAt
        };

        // Assert
        edge.Should().BeEquivalentTo(new
        {
            FromNodeId = fromNodeId,
            ToNodeId = toNodeId,
            RelationshipType = "REQUIRES",
            Weight = 0.85,
            Context = "The manual requires a torque check.",
            CreatedAtUtc = createdAt
        });
    }

    [Fact]
    public void IndexedArtifact_WhenIndexingProgressIsReported_RetainsArtifactState()
    {
        // Arrange
        var artifactId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var processedAt = DateTimeOffset.Parse("2026-07-12T12:00:00+00:00");

        // Act
        var artifact = new IndexedArtifact
        {
            IndexedArtifactId = artifactId,
            IngestionJobId = jobId,
            UploadId = "upload-123",
            ArtifactType = "search-chunks",
            BlobContainer = "manuals",
            BlobPath = "upload-123/chunks.jsonl",
            SourceFileName = "cbr600rr.pdf",
            State = IndexedArtifactState.PartiallyIndexed,
            ExpectedChunkCount = 100,
            IndexedChunkCount = 95,
            FailedChunkCount = 5,
            LastProcessedAtUtc = processedAt,
            FailureReason = "Five chunks exceeded the source size limit.",
            CreatedAtUtc = processedAt,
            UpdatedAtUtc = processedAt
        };

        // Assert
        artifact.Should().BeEquivalentTo(new
        {
            IndexedArtifactId = artifactId,
            IngestionJobId = jobId,
            UploadId = "upload-123",
            ArtifactType = "search-chunks",
            BlobContainer = "manuals",
            BlobPath = "upload-123/chunks.jsonl",
            SourceFileName = "cbr600rr.pdf",
            State = IndexedArtifactState.PartiallyIndexed,
            ExpectedChunkCount = 100,
            IndexedChunkCount = 95,
            FailedChunkCount = 5,
            LastProcessedAtUtc = processedAt,
            FailureReason = "Five chunks exceeded the source size limit.",
            CreatedAtUtc = processedAt,
            UpdatedAtUtc = processedAt
        });
    }

    [Fact]
    public void IndexedChunk_WhenIndexingFails_RetainsLocationAndFailureDetails()
    {
        // Arrange
        var artifactId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var processedAt = DateTimeOffset.Parse("2026-07-12T12:00:00+00:00");

        // Act
        var chunk = new IndexedChunk
        {
            ChunkId = "chunk-42",
            IndexedArtifactId = artifactId,
            IngestionJobId = jobId,
            UploadId = "upload-123",
            SourceFileName = "cbr600rr.pdf",
            PageNumber = 42,
            ChunkIndex = 7,
            Stage = "embedding",
            Status = ChunkIndexStatus.Failed,
            ProcessedAtUtc = processedAt,
            FailureReason = "Embedding service rejected the chunk."
        };

        // Assert
        chunk.Should().BeEquivalentTo(new
        {
            ChunkId = "chunk-42",
            IndexedArtifactId = artifactId,
            IngestionJobId = jobId,
            UploadId = "upload-123",
            SourceFileName = "cbr600rr.pdf",
            PageNumber = 42,
            ChunkIndex = 7,
            Stage = "embedding",
            Status = ChunkIndexStatus.Failed,
            ProcessedAtUtc = processedAt,
            FailureReason = "Embedding service rejected the chunk."
        });
    }
}
