using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Contracts.Models.DTOs.Graph;
using MotorcycleRAG.Persistence.Sql;
using MotorcycleRAG.Persistence.Sql.Repositories;

namespace MotorcycleRAG.UnitTests.Persistence.Sql.Repositories;

[SuppressMessage(
    "Reliability",
    "CA2000:Dispose objects before losing scope",
    Justification = "The repository under test owns and disposes each supplied fake connection and queued data reader.")]
public sealed class SqlGraphRepositoryTests
{
    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenConnectionFactoryIsNull()
    {
        var act = () => new SqlGraphRepository(null!, NullLogger<SqlGraphRepository>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        var factory = new Mock<ISqlConnectionFactory>();

        var act = () => new SqlGraphRepository(factory.Object, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task UpsertNodeAsync_ShouldThrowArgumentNullException_WhenNodeIsNull()
    {
        var sut = CreateSut();

        var act = async () => await sut.UpsertNodeAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("node");
    }

    [Fact]
    public async Task UpsertNodeAsync_ShouldExecuteMergeCommand_WhenNodeIsValid()
    {
        var node = CreateNode();
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(
            1,
            command =>
            {
                command.CommandText.Should().Contain("MERGE [dbo].[GraphNode] AS target");
                command.Parameters["Id"].Should().Be(node.Id);
                command.Parameters["Name"].Should().Be(node.Name);
                command.Parameters["Type"].Should().Be(node.Type);
                command.Parameters["Description"].Should().Be(node.Description);
                command.Parameters["SourceDocumentId"].Should().Be(node.SourceDocumentId);
                command.Parameters["CreatedAtUtc"].Should().Be(node.CreatedAtUtc.UtcDateTime);
                command.Parameters["UpdatedAtUtc"].Should().Be(node.UpdatedAtUtc!.Value.UtcDateTime);
            });
        var sut = CreateSut(connection);

        await sut.UpsertNodeAsync(node);

        connection.ExecutedCommands.Should().ContainSingle();
    }

    [Fact]
    public async Task UpsertNodeAsync_ShouldWrapDatabaseFailures()
    {
        var node = CreateNode();
        var connection = new FakeDbConnection();
        var expected = new DataException("write failed");
        connection.EnqueueNonQueryException(expected);
        var sut = CreateSut(connection);

        var act = async () => await sut.UpsertNodeAsync(node);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be($"Failed to upsert graph node {node.Id}");
        exception.Which.InnerException.Should().Be(expected);
    }

    [Fact]
    public async Task UpsertNodesAsync_ShouldThrowArgumentNullException_WhenNodesIsNull()
    {
        var sut = CreateSut();

        var act = async () => await sut.UpsertNodesAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("nodes");
    }

    [Fact]
    public async Task UpsertNodesAsync_ShouldReturnEarly_WhenNodesAreEmpty()
    {
        var factory = new Mock<ISqlConnectionFactory>(MockBehavior.Strict);
        var sut = new SqlGraphRepository(factory.Object, NullLogger<SqlGraphRepository>.Instance);

        await sut.UpsertNodesAsync(Array.Empty<GraphNodeDto>());

        factory.Verify(x => x.CreateOpenConnectionAsync(), Times.Never);
    }

    [Fact]
    public async Task UpsertNodesAsync_ShouldCommitTransaction_WhenBatchSucceeds()
    {
        var nodes = new[] { CreateNode(), CreateNode(name: "Rear Suspension") };
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(
            1,
            command =>
            {
                command.CommandText.Should().Contain("MERGE [dbo].[GraphNode] AS target");
                command.HasTransaction.Should().BeTrue();
                command.Parameters["Id"].Should().Be(nodes[0].Id);
            });
        connection.EnqueueNonQuery(
            1,
            command =>
            {
                command.HasTransaction.Should().BeTrue();
                command.Parameters["Id"].Should().Be(nodes[1].Id);
            });
        var sut = CreateSut(connection);

        await sut.UpsertNodesAsync(nodes);

        connection.BeginTransactionCount.Should().Be(1);
        connection.LastTransaction.Should().NotBeNull();
        connection.LastTransaction!.CommitCount.Should().Be(1);
        connection.LastTransaction.RollbackCount.Should().Be(0);
        connection.ExecutedCommands.Should().HaveCount(2);
    }

    [Fact]
    public async Task UpsertNodesAsync_ShouldRollbackTransaction_WhenBatchFailsMidStream()
    {
        var nodes = new[] { CreateNode(), CreateNode(name: "Chain Tension") };
        var connection = new FakeDbConnection();
        var expected = new DataException("second write failed");
        connection.EnqueueNonQuery(1);
        connection.EnqueueNonQueryException(expected);
        var sut = CreateSut(connection);

        var act = async () => await sut.UpsertNodesAsync(nodes);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be($"Failed to upsert {nodes.Length} graph nodes in batch");
        exception.Which.InnerException.Should().Be(expected);
        connection.BeginTransactionCount.Should().Be(1);
        connection.LastTransaction.Should().NotBeNull();
        connection.LastTransaction!.CommitCount.Should().Be(0);
        connection.LastTransaction.RollbackCount.Should().Be(1);
        connection.ExecutedCommands.Should().HaveCount(2);
    }

    [Fact]
    public async Task UpsertEdgeAsync_ShouldThrowArgumentNullException_WhenEdgeIsNull()
    {
        var sut = CreateSut();

        var act = async () => await sut.UpsertEdgeAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("edge");
    }

    [Fact]
    public async Task UpsertEdgeAsync_ShouldExecuteInsertWhenEdgeIsValid()
    {
        var edge = CreateEdge();
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(
            1,
            command =>
            {
                command.CommandText.Should().Contain("INSERT INTO [dbo].[GraphEdge]");
                command.Parameters["RelationshipType"].Should().Be(edge.RelationshipType);
                command.Parameters["Weight"].Should().Be(edge.Weight);
                command.Parameters["Context"].Should().Be(edge.Context);
                command.Parameters["CreatedAtUtc"].Should().Be(edge.CreatedAtUtc.UtcDateTime);
                command.Parameters["FromNodeId"].Should().Be(edge.FromNodeId);
                command.Parameters["ToNodeId"].Should().Be(edge.ToNodeId);
            });
        var sut = CreateSut(connection);

        await sut.UpsertEdgeAsync(edge);

        connection.ExecutedCommands.Should().ContainSingle();
    }

    [Fact]
    public async Task UpsertEdgeAsync_ShouldWrapDatabaseFailures()
    {
        var edge = CreateEdge();
        var connection = new FakeDbConnection();
        var expected = new DataException("edge insert failed");
        connection.EnqueueNonQueryException(expected);
        var sut = CreateSut(connection);

        var act = async () => await sut.UpsertEdgeAsync(edge);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be($"Failed to upsert graph edge from {edge.FromNodeId} to {edge.ToNodeId}");
        exception.Which.InnerException.Should().Be(expected);
    }

    [Fact]
    public async Task UpsertEdgesAsync_ShouldThrowArgumentNullException_WhenEdgesIsNull()
    {
        var sut = CreateSut();

        var act = async () => await sut.UpsertEdgesAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("edges");
    }

    [Fact]
    public async Task UpsertEdgesAsync_ShouldReturnEarly_WhenEdgesAreEmpty()
    {
        var factory = new Mock<ISqlConnectionFactory>(MockBehavior.Strict);
        var sut = new SqlGraphRepository(factory.Object, NullLogger<SqlGraphRepository>.Instance);

        await sut.UpsertEdgesAsync(Array.Empty<GraphEdgeDto>());

        factory.Verify(x => x.CreateOpenConnectionAsync(), Times.Never);
    }

    [Fact]
    public async Task UpsertEdgesAsync_ShouldCommitTransaction_WhenBatchSucceeds()
    {
        var edges = new[] { CreateEdge(), CreateEdge(relationshipType: "PART_OF") };
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(
            1,
            command =>
            {
                command.HasTransaction.Should().BeTrue();
                command.Parameters["RelationshipType"].Should().Be(edges[0].RelationshipType);
            });
        connection.EnqueueNonQuery(
            1,
            command =>
            {
                command.HasTransaction.Should().BeTrue();
                command.Parameters["RelationshipType"].Should().Be(edges[1].RelationshipType);
            });
        var sut = CreateSut(connection);

        await sut.UpsertEdgesAsync(edges);

        connection.BeginTransactionCount.Should().Be(1);
        connection.LastTransaction.Should().NotBeNull();
        connection.LastTransaction!.CommitCount.Should().Be(1);
        connection.LastTransaction.RollbackCount.Should().Be(0);
        connection.ExecutedCommands.Should().HaveCount(2);
    }

    [Fact]
    public async Task UpsertEdgesAsync_ShouldRollbackTransaction_WhenBatchFailsMidStream()
    {
        var edges = new[] { CreateEdge(), CreateEdge(relationshipType: "USES") };
        var connection = new FakeDbConnection();
        var expected = new DataException("second edge failed");
        connection.EnqueueNonQuery(1);
        connection.EnqueueNonQueryException(expected);
        var sut = CreateSut(connection);

        var act = async () => await sut.UpsertEdgesAsync(edges);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be($"Failed to upsert {edges.Length} graph edges in batch");
        exception.Which.InnerException.Should().Be(expected);
        connection.BeginTransactionCount.Should().Be(1);
        connection.LastTransaction.Should().NotBeNull();
        connection.LastTransaction!.CommitCount.Should().Be(0);
        connection.LastTransaction.RollbackCount.Should().Be(1);
        connection.ExecutedCommands.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetNodesByDocumentAsync_ShouldMapReturnedRows()
    {
        var documentId = Guid.NewGuid();
        var firstCreated = new DateTimeOffset(2026, 7, 10, 14, 0, 0, TimeSpan.Zero);
        var secondCreated = firstCreated.AddMinutes(5);
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            CreateReader(
                CreateNodeRow(
                    id: Guid.NewGuid(),
                    name: "Oil Change",
                    type: "Procedure",
                    description: "Change the engine oil",
                    sourceDocumentId: documentId,
                    createdAtUtc: firstCreated,
                    updatedAtUtc: firstCreated.AddHours(1)),
                CreateNodeRow(
                    id: Guid.NewGuid(),
                    name: "Torque Spec",
                    type: "Specification",
                    description: null,
                    sourceDocumentId: documentId,
                    createdAtUtc: secondCreated,
                    updatedAtUtc: null)),
            command =>
            {
                command.CommandText.Should().Contain("WHERE  [SourceDocumentId] = @SourceDocumentId");
                command.Parameters["SourceDocumentId"].Should().Be(documentId);
            });
        var sut = CreateSut(connection);

        var results = await sut.GetNodesByDocumentAsync(documentId);

        results.Should().HaveCount(2);
        results[0].Name.Should().Be("Oil Change");
        results[0].Type.Should().Be("Procedure");
        results[0].Description.Should().Be("Change the engine oil");
        results[0].SourceDocumentId.Should().Be(documentId);
        results[0].CreatedAtUtc.Should().Be(firstCreated);
        results[0].UpdatedAtUtc.Should().Be(firstCreated.AddHours(1));
        results[1].Name.Should().Be("Torque Spec");
        results[1].UpdatedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task GetNodesByDocumentAsync_ShouldReturnEmpty_WhenNoRowsExist()
    {
        var documentId = Guid.NewGuid();
        var connection = new FakeDbConnection();
        connection.EnqueueReader(CreateReaderWithSchema("Id", "Name", "Type", "Description", "SourceDocumentId", "CreatedAtUtc", "UpdatedAtUtc"));
        var sut = CreateSut(connection);

        var results = await sut.GetNodesByDocumentAsync(documentId);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetNodesByDocumentAsync_ShouldWrapDatabaseFailures()
    {
        var documentId = Guid.NewGuid();
        var connection = new FakeDbConnection();
        var expected = new DataException("query failed");
        connection.EnqueueReaderException(expected);
        var sut = CreateSut(connection);

        var act = async () => await sut.GetNodesByDocumentAsync(documentId);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be($"Failed to get graph nodes for document {documentId}");
        exception.Which.InnerException.Should().Be(expected);
    }

    [Fact]
    public async Task DeleteByDocumentAsync_ShouldExecuteInTransactionAndCommit()
    {
        var documentId = Guid.NewGuid();
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(
            3,
            command =>
            {
                command.CommandText.Should().Contain("SELECT [Id] INTO #NodesToDelete");
                command.CommandText.Should().Contain("DELETE e");
                command.CommandText.Should().Contain("DELETE n");
                command.Parameters["SourceDocumentId"].Should().Be(documentId);
                command.HasTransaction.Should().BeTrue();
                command.CommandTimeout.Should().Be(90);
            });
        var sut = CreateSut(connection);

        await sut.DeleteByDocumentAsync(documentId);

        connection.BeginTransactionCount.Should().Be(1);
        connection.LastTransaction.Should().NotBeNull();
        connection.LastTransaction!.CommitCount.Should().Be(1);
        connection.LastTransaction.RollbackCount.Should().Be(0);
        connection.ExecutedCommands.Should().ContainSingle();
    }

    [Fact]
    public async Task DeleteByDocumentAsync_ShouldRollbackAndWrapDatabaseFailures()
    {
        var documentId = Guid.NewGuid();
        var connection = new FakeDbConnection();
        var expected = new DataException("delete failed");
        connection.EnqueueNonQueryException(expected);
        var sut = CreateSut(connection);

        var act = async () => await sut.DeleteByDocumentAsync(documentId);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be($"Failed to delete graph data for document {documentId}");
        exception.Which.InnerException.Should().Be(expected);
        connection.BeginTransactionCount.Should().Be(1);
        connection.LastTransaction.Should().NotBeNull();
        connection.LastTransaction!.CommitCount.Should().Be(0);
        connection.LastTransaction.RollbackCount.Should().Be(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task SearchNodesAsync_ShouldThrowArgumentException_WhenSearchTermIsBlank(string? searchTerm)
    {
        var sut = CreateSut();

        var act = async () => await sut.SearchNodesAsync(searchTerm!, null, 5);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName(nameof(searchTerm));
    }

    [Fact]
    public async Task SearchNodesAsync_ShouldDefaultMaxResultsAndApplyTypeFilter_WhenProvided()
    {
        var createdAtUtc = new DateTimeOffset(2026, 7, 10, 15, 0, 0, TimeSpan.Zero);
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            CreateReader(CreateNodeRow(
                name: "Brake Pad",
                type: "Component",
                createdAtUtc: createdAtUtc)),
            command =>
            {
                command.CommandText.Should().Contain("AND [Type] = @TypeFilter");
                command.Parameters["MaxResults"].Should().Be(20);
                command.Parameters["Pattern"].Should().Be("%brake%");
                command.Parameters["TypeFilter"].Should().Be("Component");
            });
        var sut = CreateSut(connection);

        var results = await sut.SearchNodesAsync("brake", "Component", 0);

        results.Should().ContainSingle();
        results[0].Name.Should().Be("Brake Pad");
        results[0].Type.Should().Be("Component");
    }

    [Fact]
    public async Task SearchNodesAsync_ShouldOmitTypeFilter_WhenNotProvided()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            CreateReader(CreateNodeRow(name: "Cam Chain", type: "Component")),
            command =>
            {
                command.CommandText.Should().Contain("WHERE [Name] LIKE @Pattern");
                command.CommandText.Should().NotContain("AND [Type] = @TypeFilter");
                command.Parameters["MaxResults"].Should().Be(7);
                command.Parameters.ContainsKey("TypeFilter").Should().BeFalse();
            });
        var sut = CreateSut(connection);

        var results = await sut.SearchNodesAsync("cam", null, 7);

        results.Should().ContainSingle();
        results[0].Name.Should().Be("Cam Chain");
    }

    [Fact]
    public async Task SearchNodesAsync_ShouldWrapDatabaseFailures()
    {
        var connection = new FakeDbConnection();
        var expected = new DataException("search failed");
        connection.EnqueueReaderException(expected);
        var sut = CreateSut(connection);

        var act = async () => await sut.SearchNodesAsync("brake", null, 5);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Failed to search graph nodes for term 'brake'");
        exception.Which.InnerException.Should().Be(expected);
    }

    [Fact]
    public async Task GetNeighboursAsync_ShouldMapTraversalRows_WhenRelationshipFilterIsProvided()
    {
        var nodeId = Guid.NewGuid();
        var fromCreatedAtUtc = new DateTimeOffset(2026, 7, 10, 16, 0, 0, TimeSpan.Zero);
        var toCreatedAtUtc = fromCreatedAtUtc.AddMinutes(10);
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            CreateReader(CreateTraversalRow(
                fromId: nodeId,
                fromName: "Engine",
                fromType: "Component",
                fromDescription: "Main engine",
                fromSourceDocumentId: Guid.NewGuid(),
                fromCreatedAtUtc: fromCreatedAtUtc,
                fromUpdatedAtUtc: fromCreatedAtUtc.AddHours(1),
                relationshipType: "USES",
                weight: 0.9,
                context: "Lubrication path",
                toId: Guid.NewGuid(),
                toName: "Oil Filter",
                toType: "Component",
                toDescription: "Filter assembly",
                toSourceDocumentId: Guid.NewGuid(),
                toCreatedAtUtc: toCreatedAtUtc,
                toUpdatedAtUtc: null)),
            command =>
            {
                command.CommandText.Should().Contain("AND e.[RelationshipType] = @RelFilter;");
                command.Parameters["NodeId"].Should().Be(nodeId);
                command.Parameters["RelFilter"].Should().Be("USES");
            });
        var sut = CreateSut(connection);

        var results = await sut.GetNeighboursAsync(nodeId, "USES");

        results.Should().ContainSingle();
        results[0].FromNode.Id.Should().Be(nodeId);
        results[0].FromNode.Name.Should().Be("Engine");
        results[0].RelationshipType.Should().Be("USES");
        results[0].Weight.Should().Be(0.9);
        results[0].Context.Should().Be("Lubrication path");
        results[0].ToNode.Name.Should().Be("Oil Filter");
        results[0].ToNode.CreatedAtUtc.Should().Be(toCreatedAtUtc);
        results[0].ToNode.UpdatedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task GetNeighboursAsync_ShouldOmitRelationshipFilter_WhenNotProvided()
    {
        var nodeId = Guid.NewGuid();
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            CreateReader(CreateTraversalRow(fromId: nodeId, relationshipType: "PART_OF")),
            command =>
            {
                command.CommandText.Should().Contain("AND n1.[Id] = @NodeId;");
                command.CommandText.Should().NotContain("AND e.[RelationshipType] = @RelFilter;");
                command.Parameters.ContainsKey("RelFilter").Should().BeFalse();
            });
        var sut = CreateSut(connection);

        var results = await sut.GetNeighboursAsync(nodeId, null);

        results.Should().ContainSingle();
        results[0].RelationshipType.Should().Be("PART_OF");
    }

    [Fact]
    public async Task GetNeighboursAsync_ShouldWrapDatabaseFailures()
    {
        var nodeId = Guid.NewGuid();
        var connection = new FakeDbConnection();
        var expected = new DataException("neighbour query failed");
        connection.EnqueueReaderException(expected);
        var sut = CreateSut(connection);

        var act = async () => await sut.GetNeighboursAsync(nodeId, null);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be($"Failed to get neighbours for node {nodeId}");
        exception.Which.InnerException.Should().Be(expected);
    }

    [Fact]
    public async Task FindPathsAsync_ShouldDefaultLimitsAndMapPathRows()
    {
        var sourceNodeId = Guid.NewGuid();
        var intermediateNodeId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            CreateReader(CreatePathRow(
                sourceId: sourceNodeId,
                sourceName: "Battery",
                sourceType: "Component",
                sourceDescription: "12V battery",
                firstRelationship: "POWERS",
                intermediateId: intermediateNodeId,
                intermediateName: "Starter Motor",
                intermediateType: "Component",
                intermediateDescription: "Electric starter",
                secondRelationship: "DRIVES",
                targetId: targetNodeId,
                targetName: "Crankshaft",
                targetType: "Component",
                targetDescription: "Crank output")),
            command =>
            {
                command.Parameters["SourceNodeId"].Should().Be(sourceNodeId);
                command.Parameters["MaxDepth"].Should().Be(2);
                command.Parameters["MaxResults"].Should().Be(50);
            });
        var sut = CreateSut(connection);

        var results = await sut.FindPathsAsync(sourceNodeId, 0, 0);

        results.Should().ContainSingle();
        results[0].SourceNode.Id.Should().Be(sourceNodeId);
        results[0].SourceNode.Name.Should().Be("Battery");
        results[0].FirstRelationship.Should().Be("POWERS");
        results[0].IntermediateNode.Id.Should().Be(intermediateNodeId);
        results[0].SecondRelationship.Should().Be("DRIVES");
        results[0].TargetNode.Id.Should().Be(targetNodeId);
        results[0].TargetNode.Description.Should().Be("Crank output");
    }

    [Fact]
    public async Task FindPathsAsync_ShouldWrapDatabaseFailures()
    {
        var sourceNodeId = Guid.NewGuid();
        var connection = new FakeDbConnection();
        var expected = new DataException("path query failed");
        connection.EnqueueReaderException(expected);
        var sut = CreateSut(connection);

        var act = async () => await sut.FindPathsAsync(sourceNodeId, 3, 5);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be($"Failed to find paths from node {sourceNodeId}");
        exception.Which.InnerException.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task GetEdgesByTypeAsync_ShouldThrowArgumentException_WhenRelationshipTypeIsBlank(string? relationshipType)
    {
        var sut = CreateSut();

        var act = async () => await sut.GetEdgesByTypeAsync(relationshipType!, 5);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName(nameof(relationshipType));
    }

    [Fact]
    public async Task GetEdgesByTypeAsync_ShouldDefaultMaxResultsAndMapTraversalRows()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            CreateReader(CreateTraversalRow(relationshipType: "REQUIRES", weight: 0.75, context: "Service interval dependency")),
            command =>
            {
                command.CommandText.Should().Contain("AND e.[RelationshipType] = @RelType;");
                command.Parameters["RelType"].Should().Be("REQUIRES");
                command.Parameters["MaxResults"].Should().Be(50);
            });
        var sut = CreateSut(connection);

        var results = await sut.GetEdgesByTypeAsync("REQUIRES", -1);

        results.Should().ContainSingle();
        results[0].RelationshipType.Should().Be("REQUIRES");
        results[0].Weight.Should().Be(0.75);
        results[0].Context.Should().Be("Service interval dependency");
    }

    [Fact]
    public async Task GetEdgesByTypeAsync_ShouldWrapDatabaseFailures()
    {
        var connection = new FakeDbConnection();
        var expected = new DataException("edge traversal failed");
        connection.EnqueueReaderException(expected);
        var sut = CreateSut(connection);

        var act = async () => await sut.GetEdgesByTypeAsync("REQUIRES", 10);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Failed to get edges by type 'REQUIRES'");
        exception.Which.InnerException.Should().Be(expected);
    }

    private static SqlGraphRepository CreateSut(FakeDbConnection? connection = null)
    {
        var factory = new Mock<ISqlConnectionFactory>();
        factory
            .Setup(x => x.CreateOpenConnectionAsync())
            .ReturnsAsync(connection ?? new FakeDbConnection());
        return new SqlGraphRepository(factory.Object, NullLogger<SqlGraphRepository>.Instance);
    }

    private static GraphNodeDto CreateNode(
        Guid? id = null,
        string name = "Front Brake",
        string type = "Component",
        string? description = "Front brake assembly",
        Guid? sourceDocumentId = null,
        DateTimeOffset? createdAtUtc = null,
        DateTimeOffset? updatedAtUtc = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            Type = type,
            Description = description,
            SourceDocumentId = sourceDocumentId ?? Guid.NewGuid(),
            CreatedAtUtc = createdAtUtc ?? new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero),
            UpdatedAtUtc = updatedAtUtc ?? new DateTimeOffset(2026, 7, 10, 13, 0, 0, TimeSpan.Zero)
        };

    private static GraphEdgeDto CreateEdge(
        Guid? fromNodeId = null,
        Guid? toNodeId = null,
        string relationshipType = "REQUIRES",
        double weight = 0.8,
        string? context = "Depends on related component",
        DateTimeOffset? createdAtUtc = null) =>
        new()
        {
            FromNodeId = fromNodeId ?? Guid.NewGuid(),
            ToNodeId = toNodeId ?? Guid.NewGuid(),
            RelationshipType = relationshipType,
            Weight = weight,
            Context = context,
            CreatedAtUtc = createdAtUtc ?? new DateTimeOffset(2026, 7, 10, 12, 30, 0, TimeSpan.Zero)
        };

    private static Dictionary<string, object?> CreateNodeRow(
        Guid? id = null,
        string name = "Front Brake",
        string type = "Component",
        string? description = "Front brake assembly",
        Guid? sourceDocumentId = null,
        DateTimeOffset? createdAtUtc = null,
        DateTimeOffset? updatedAtUtc = null) =>
        new()
        {
            ["Id"] = id ?? Guid.NewGuid(),
            ["Name"] = name,
            ["Type"] = type,
            ["Description"] = description,
            ["SourceDocumentId"] = sourceDocumentId,
            ["CreatedAtUtc"] = createdAtUtc ?? new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero),
            ["UpdatedAtUtc"] = updatedAtUtc
        };

    private static Dictionary<string, object?> CreateTraversalRow(
        Guid? fromId = null,
        string fromName = "Source Node",
        string fromType = "Procedure",
        string? fromDescription = "Source description",
        Guid? fromSourceDocumentId = null,
        DateTimeOffset? fromCreatedAtUtc = null,
        DateTimeOffset? fromUpdatedAtUtc = null,
        string relationshipType = "RELATED_TO",
        double weight = 0.5,
        string? context = "Linked in manual",
        Guid? toId = null,
        string toName = "Target Node",
        string toType = "Component",
        string? toDescription = "Target description",
        Guid? toSourceDocumentId = null,
        DateTimeOffset? toCreatedAtUtc = null,
        DateTimeOffset? toUpdatedAtUtc = null) =>
        new()
        {
            ["FromId"] = fromId ?? Guid.NewGuid(),
            ["FromName"] = fromName,
            ["FromType"] = fromType,
            ["FromDescription"] = fromDescription,
            ["FromSourceDocumentId"] = fromSourceDocumentId ?? Guid.NewGuid(),
            ["FromCreatedAtUtc"] = fromCreatedAtUtc ?? new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero),
            ["FromUpdatedAtUtc"] = fromUpdatedAtUtc,
            ["RelationshipType"] = relationshipType,
            ["Weight"] = weight,
            ["Context"] = context,
            ["ToId"] = toId ?? Guid.NewGuid(),
            ["ToName"] = toName,
            ["ToType"] = toType,
            ["ToDescription"] = toDescription,
            ["ToSourceDocumentId"] = toSourceDocumentId ?? Guid.NewGuid(),
            ["ToCreatedAtUtc"] = toCreatedAtUtc ?? new DateTimeOffset(2026, 7, 10, 12, 5, 0, TimeSpan.Zero),
            ["ToUpdatedAtUtc"] = toUpdatedAtUtc
        };

    private static Dictionary<string, object?> CreatePathRow(
        Guid? sourceId = null,
        string sourceName = "Source",
        string sourceType = "Procedure",
        string? sourceDescription = "Source description",
        string firstRelationship = "LEADS_TO",
        Guid? intermediateId = null,
        string intermediateName = "Intermediate",
        string intermediateType = "Component",
        string? intermediateDescription = "Intermediate description",
        string secondRelationship = "USES",
        Guid? targetId = null,
        string targetName = "Target",
        string targetType = "Specification",
        string? targetDescription = "Target description") =>
        new()
        {
            ["SourceId"] = sourceId ?? Guid.NewGuid(),
            ["SourceName"] = sourceName,
            ["SourceType"] = sourceType,
            ["SourceDescription"] = sourceDescription,
            ["FirstRelationship"] = firstRelationship,
            ["IntermediateId"] = intermediateId ?? Guid.NewGuid(),
            ["IntermediateName"] = intermediateName,
            ["IntermediateType"] = intermediateType,
            ["IntermediateDescription"] = intermediateDescription,
            ["SecondRelationship"] = secondRelationship,
            ["TargetId"] = targetId ?? Guid.NewGuid(),
            ["TargetName"] = targetName,
            ["TargetType"] = targetType,
            ["TargetDescription"] = targetDescription
        };

    private static DbDataReader CreateReader(params IReadOnlyDictionary<string, object?>[] rows)
    {
        if (rows.Length == 0)
        {
            return CreateReaderWithSchema("Id");
        }

        var columnNames = rows.SelectMany(static row => row.Keys).Distinct(StringComparer.Ordinal).ToArray();
        return CreateReaderCore(columnNames, rows);
    }

    private static DbDataReader CreateReaderWithSchema(params string[] columnNames)
    {
        return CreateReaderCore(columnNames, Array.Empty<IReadOnlyDictionary<string, object?>>());
    }

    private static DbDataReader CreateReaderCore(
        IReadOnlyList<string> columnNames,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        var table = new DataTable();
        foreach (var columnName in columnNames)
        {
            table.Columns.Add(columnName, typeof(object));
        }

        foreach (var row in rows)
        {
            var dataRow = table.NewRow();
            foreach (var columnName in columnNames)
            {
                dataRow[columnName] = row.TryGetValue(columnName, out var value) ? value ?? DBNull.Value : DBNull.Value;
            }

            table.Rows.Add(dataRow);
        }

        return table.CreateDataReader();
    }

    private sealed class FakeDbConnection : DbConnection
    {
        private readonly Queue<CommandPlan> _plans = new();

        public List<ExecutedCommand> ExecutedCommands { get; } = [];
        public int BeginTransactionCount { get; private set; }
        public FakeDbTransaction? LastTransaction { get; private set; }

        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "Fake";
        public override string DataSource => "Fake";
        public override string ServerVersion => "1.0";
        public override ConnectionState State => ConnectionState.Open;

        public void EnqueueNonQuery(int affectedRows, Action<ExecutedCommand>? assert = null)
        {
            _plans.Enqueue(new CommandPlan(CommandKind.NonQuery, () => affectedRows, assert));
        }

        public void EnqueueNonQueryException(Exception exception, Action<ExecutedCommand>? assert = null)
        {
            _plans.Enqueue(new CommandPlan(CommandKind.NonQuery, () => throw exception, assert));
        }

        public void EnqueueReader(DbDataReader reader, Action<ExecutedCommand>? assert = null)
        {
            _plans.Enqueue(new CommandPlan(CommandKind.Reader, () => reader, assert));
        }

        public void EnqueueReaderException(Exception exception, Action<ExecutedCommand>? assert = null)
        {
            _plans.Enqueue(new CommandPlan(CommandKind.Reader, () => throw exception, assert));
        }

        public override void ChangeDatabase(string databaseName) { }
        public override void Close() { }
        public override void Open() { }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            BeginTransactionCount++;
            LastTransaction = new FakeDbTransaction(this, isolationLevel);
            return LastTransaction;
        }

        protected override DbCommand CreateDbCommand() => new FakeDbCommand(this);

        internal object? Execute(CommandKind kind, FakeDbCommand command)
        {
            _plans.Should().NotBeEmpty("every repository call in these tests should have a planned DB response");
            var plan = _plans.Dequeue();
            plan.Kind.Should().Be(kind);

            var executed = new ExecutedCommand(
                command.CommandText,
                command.GetParameters(),
                command.CurrentTransaction is not null,
                command.CommandTimeout);
            ExecutedCommands.Add(executed);
            plan.Assert?.Invoke(executed);
            return plan.ResultFactory();
        }
    }

    private sealed class FakeDbTransaction : DbTransaction
    {
        private readonly FakeDbConnection _connection;

        public FakeDbTransaction(FakeDbConnection connection, IsolationLevel isolationLevel)
        {
            _connection = connection;
            IsolationLevel = isolationLevel;
        }

        public override IsolationLevel IsolationLevel { get; }
        protected override DbConnection DbConnection => _connection;
        public int CommitCount { get; private set; }
        public int RollbackCount { get; private set; }

        public override void Commit()
        {
            CommitCount++;
        }

        public override void Rollback()
        {
            RollbackCount++;
        }
    }

    private sealed class FakeDbCommand : DbCommand
    {
        private readonly FakeDbConnection _connection;
        private readonly FakeDbParameterCollection _parameters = new();

        public FakeDbCommand(FakeDbConnection connection)
        {
            _connection = connection;
        }

        [AllowNull]
        public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }

        [AllowNull]
        protected override DbConnection DbConnection
        {
            get => _connection;
            set => throw new NotSupportedException();
        }

        protected override DbParameterCollection DbParameterCollection => _parameters;
        protected override DbTransaction? DbTransaction { get; set; }
        internal DbTransaction? CurrentTransaction => DbTransaction;

        public override void Cancel() { }
        public override int ExecuteNonQuery() => (int)(_connection.Execute(CommandKind.NonQuery, this) ?? 0);
        public override object? ExecuteScalar() => throw new NotSupportedException();
        public override void Prepare() { }
        protected override DbParameter CreateDbParameter() => new FakeDbParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
            (DbDataReader)(_connection.Execute(CommandKind.Reader, this)
                ?? throw new InvalidOperationException("Reader result was null."));

        internal Dictionary<string, object?> GetParameters() =>
            _parameters
                .Cast<FakeDbParameter>()
            .ToDictionary(parameter => parameter.ParameterName ?? string.Empty, parameter => parameter.Value, StringComparer.Ordinal);
    }

    private sealed class FakeDbParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> _parameters = [];

        public override int Count => _parameters.Count;
        public override object SyncRoot => ((ICollection)_parameters).SyncRoot;

        public override int Add(object value)
        {
            _parameters.Add((DbParameter)value);
            return _parameters.Count - 1;
        }

        public override void AddRange(Array values)
        {
            foreach (var value in values)
            {
                Add(value!);
            }
        }

        public override void Clear() => _parameters.Clear();
        public override bool Contains(object value) => _parameters.Contains((DbParameter)value);
        public override bool Contains(string value) => _parameters.Any(parameter => parameter.ParameterName == value);
        public override void CopyTo(Array array, int index) => ((ICollection)_parameters).CopyTo(array, index);
        public override IEnumerator GetEnumerator() => _parameters.GetEnumerator();
        public override int IndexOf(object value) => _parameters.IndexOf((DbParameter)value);
        public override int IndexOf(string parameterName) => _parameters.FindIndex(parameter => parameter.ParameterName == parameterName);
        public override void Insert(int index, object value) => _parameters.Insert(index, (DbParameter)value);
        public override void Remove(object value) => _parameters.Remove((DbParameter)value);
        public override void RemoveAt(int index) => _parameters.RemoveAt(index);

        public override void RemoveAt(string parameterName)
        {
            var index = IndexOf(parameterName);
            if (index >= 0)
            {
                _parameters.RemoveAt(index);
            }
        }

        protected override DbParameter GetParameter(int index) => _parameters[index];
        protected override DbParameter GetParameter(string parameterName) => _parameters[IndexOf(parameterName)];
        protected override void SetParameter(int index, DbParameter value) => _parameters[index] = value;

        protected override void SetParameter(string parameterName, DbParameter value)
        {
            var index = IndexOf(parameterName);
            if (index >= 0)
            {
                _parameters[index] = value;
            }
            else
            {
                _parameters.Add(value);
            }
        }
    }

    private sealed class FakeDbParameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;
        public override bool IsNullable { get; set; }
        [AllowNull]
        public override string ParameterName { get; set; } = string.Empty;
        [AllowNull]
        public override string SourceColumn { get; set; } = string.Empty;
        public override object? Value { get; set; }
        public override bool SourceColumnNullMapping { get; set; }
        public override int Size { get; set; }
        public override void ResetDbType() { }
    }

    private sealed record CommandPlan(
        CommandKind Kind,
        Func<object?> ResultFactory,
        Action<ExecutedCommand>? Assert);

    private sealed record ExecutedCommand(
        string CommandText,
        IReadOnlyDictionary<string, object?> Parameters,
        bool HasTransaction,
        int CommandTimeout);

    private enum CommandKind
    {
        NonQuery,
        Reader
    }
}
