using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;
using MotorcycleRAG.Persistence.Sql;
using MotorcycleRAG.Persistence.Sql.Repositories;

namespace MotorcycleRAG.UnitTests.Persistence.Sql.Repositories;

public sealed class IndexedChunkRepositoryTests
{
    // ─────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────

    [Trait("Category", "Constructor")]
    [Fact]
    public void Constructor_NullConnectionFactory_ThrowsArgumentNullException()
    {
        var act = () => new IndexedChunkRepository(null!, TestHelpers.CreateNullLogger<IndexedChunkRepository>());

        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Trait("Category", "Constructor")]
    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var factory = new Mock<ISqlConnectionFactory>();

        var act = () => new IndexedChunkRepository(factory.Object, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ─────────────────────────────────────────────────────────────────────
    // UpsertManyAsync
    // ─────────────────────────────────────────────────────────────────────

    [Trait("Category", "UpsertManyAsync")]
    [Fact]
    public async Task UpsertManyAsync_EmptyCollection_ReturnsWithoutTouchingConnection()
    {
        var factory = new Mock<ISqlConnectionFactory>();
        var sut = new IndexedChunkRepository(factory.Object, TestHelpers.CreateNullLogger<IndexedChunkRepository>());

        await sut.UpsertManyAsync(Array.Empty<IndexedChunk>());

        factory.Verify(x => x.CreateOpenConnectionAsync(), Times.Never);
    }

    [Trait("Category", "UpsertManyAsync")]
    [Fact]
    public async Task UpsertManyAsync_SingleChunk_ExecutesMergeWithExpectedParameters()
    {
        var chunk = CreateChunk();
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(1, command =>
        {
            command.CommandText.Should().Contain("MERGE [dbo].[IndexedChunks]");
            command.Parameters["ChunkId"].Should().Be(chunk.ChunkId);
            command.Parameters["IndexedArtifactId"].Should().Be(chunk.IndexedArtifactId);
            command.Parameters["IngestionJobId"].Should().Be(chunk.IngestionJobId);
            command.Parameters["UploadId"].Should().Be(chunk.UploadId);
            command.Parameters["SourceFileName"].Should().Be(chunk.SourceFileName);
            command.Parameters["PageNumber"].Should().Be(chunk.PageNumber);
            command.Parameters["ChunkIndex"].Should().Be(chunk.ChunkIndex);
            command.Parameters["Stage"].Should().Be(chunk.Stage);
            command.Parameters["Status"].Should().Be(chunk.Status.ToString());
            command.Parameters["ProcessedAtUtc"].Should().Be(chunk.ProcessedAtUtc);
            command.Parameters["FailureReason"].Should().Be(chunk.FailureReason);
        });
        var sut = CreateSut(connection);

        await sut.UpsertManyAsync(new[] { chunk });

        connection.ExecutedCommands.Should().ContainSingle();
    }

    [Trait("Category", "UpsertManyAsync")]
    [Fact]
    public async Task UpsertManyAsync_MultipleChunks_ExecutesOneCommandPerChunk()
    {
        var chunks = new[] { CreateChunk(), CreateChunk(), CreateChunk() };
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(1);
        connection.EnqueueNonQuery(1);
        connection.EnqueueNonQuery(1);
        var sut = CreateSut(connection);

        await sut.UpsertManyAsync(chunks);

        connection.ExecutedCommands.Should().HaveCount(3);
    }

    [Trait("Category", "UpsertManyAsync")]
    [Fact]
    public async Task UpsertManyAsync_ConnectionFailure_WrapsInInvalidOperationException()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.UpsertManyAsync(new[] { CreateChunk() });

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Failed to upsert indexed chunks");
        exception.Which.InnerException.Should().Be(expected);
    }

    [Trait("Category", "UpsertManyAsync")]
    [Fact]
    public async Task UpsertManyAsync_ExecuteFailure_WrapsInInvalidOperationException()
    {
        var expected = new InvalidOperationException("execute boom");
        var connection = new FakeDbConnection();
        connection.EnqueueNonQueryException(expected);
        var sut = CreateSut(connection);

        var act = async () => await sut.UpsertManyAsync(new[] { CreateChunk() });

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.InnerException.Should().Be(expected);
    }

    // ─────────────────────────────────────────────────────────────────────
    // GetByArtifactIdAsync
    // ─────────────────────────────────────────────────────────────────────

    [Trait("Category", "GetByArtifactIdAsync")]
    [Theory]
    [InlineData(ChunkIndexStatus.InProc)]
    [InlineData(ChunkIndexStatus.Complete)]
    [InlineData(ChunkIndexStatus.Failed)]
    public async Task GetByArtifactIdAsync_RowsExist_MapsAllStatuses(ChunkIndexStatus status)
    {
        var artifactId = Guid.NewGuid();
        var row = CreateChunkRow(artifactId: artifactId, status: status);
        var connection = new FakeDbConnection();
        using var reader = RepositoryTestReader.CreateReader(row);
        connection.EnqueueReader(reader, command =>
        {
            command.CommandText.Should().Contain("WHERE [IndexedArtifactId] = @IndexedArtifactId");
            command.Parameters["IndexedArtifactId"].Should().Be(artifactId);
        });
        var sut = CreateSut(connection);

        var result = await sut.GetByArtifactIdAsync(artifactId);

        result.Should().ContainSingle();
        result[0].Status.Should().Be(status);
        result[0].IndexedArtifactId.Should().Be(artifactId);
    }

    [Trait("Category", "GetByArtifactIdAsync")]
    [Fact]
    public async Task GetByArtifactIdAsync_RowsExist_MapsAllFields()
    {
        var artifactId = Guid.NewGuid();
        var ingestionJobId = Guid.NewGuid();
        var processedAt = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        var row = CreateChunkRow(
            chunkId: "chunk-42",
            artifactId: artifactId,
            ingestionJobId: ingestionJobId,
            uploadId: "upload-1",
            sourceFileName: "honda.pdf",
            pageNumber: 3,
            chunkIndex: 5,
            stage: "embedding",
            status: ChunkIndexStatus.Complete,
            processedAtUtc: processedAt,
            failureReason: null);
        var connection = new FakeDbConnection();
        using var reader = RepositoryTestReader.CreateReader(row);
        connection.EnqueueReader(reader);
        var sut = CreateSut(connection);

        var result = await sut.GetByArtifactIdAsync(artifactId);

        result.Should().ContainSingle();
        var chunk = result[0];
        chunk.ChunkId.Should().Be("chunk-42");
        chunk.IndexedArtifactId.Should().Be(artifactId);
        chunk.IngestionJobId.Should().Be(ingestionJobId);
        chunk.UploadId.Should().Be("upload-1");
        chunk.SourceFileName.Should().Be("honda.pdf");
        chunk.PageNumber.Should().Be(3);
        chunk.ChunkIndex.Should().Be(5);
        chunk.Stage.Should().Be("embedding");
        chunk.Status.Should().Be(ChunkIndexStatus.Complete);
        chunk.ProcessedAtUtc.Should().Be(processedAt);
        chunk.FailureReason.Should().BeNull();
    }

    [Trait("Category", "GetByArtifactIdAsync")]
    [Fact]
    public async Task GetByArtifactIdAsync_NoRows_ReturnsEmptyList()
    {
        var connection = new FakeDbConnection();
        using var reader = RepositoryTestReader.CreateReader();
        connection.EnqueueReader(reader);
        var sut = CreateSut(connection);

        var result = await sut.GetByArtifactIdAsync(Guid.NewGuid());

        result.Should().BeEmpty();
    }

    [Trait("Category", "GetByArtifactIdAsync")]
    [Fact]
    public async Task GetByArtifactIdAsync_ConnectionFailure_WrapsInInvalidOperationException()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetByArtifactIdAsync(Guid.NewGuid());

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Failed to get indexed chunks");
        exception.Which.InnerException.Should().Be(expected);
    }

    // ─────────────────────────────────────────────────────────────────────
    // GetByArtifactIdsAsync
    // ─────────────────────────────────────────────────────────────────────

    [Trait("Category", "GetByArtifactIdsAsync")]
    [Fact]
    public async Task GetByArtifactIdsAsync_NullCollection_ThrowsArgumentNullException()
    {
        var sut = CreateSut();

        var act = async () => await sut.GetByArtifactIdsAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("artifactIds");
    }

    [Trait("Category", "GetByArtifactIdsAsync")]
    [Fact]
    public async Task GetByArtifactIdsAsync_EmptyCollection_ReturnsEmptyWithoutTouchingConnection()
    {
        var factory = new Mock<ISqlConnectionFactory>();
        var sut = new IndexedChunkRepository(factory.Object, TestHelpers.CreateNullLogger<IndexedChunkRepository>());

        var result = await sut.GetByArtifactIdsAsync(Array.Empty<Guid>());

        result.Should().BeEmpty();
        factory.Verify(x => x.CreateOpenConnectionAsync(), Times.Never);
    }

    [Trait("Category", "GetByArtifactIdsAsync")]
    [Fact]
    public async Task GetByArtifactIdsAsync_RowsExist_MapsResultsAndPassesAllIds()
    {
        var artifactId1 = Guid.NewGuid();
        var artifactId2 = Guid.NewGuid();
        var connection = new FakeDbConnection();
        using var reader = RepositoryTestReader.CreateReader(
            CreateChunkRow(artifactId: artifactId1),
            CreateChunkRow(artifactId: artifactId2));
        connection.EnqueueReader(
            reader,
            command =>
            {
                command.CommandText.Should().Contain("WHERE [IndexedArtifactId] IN (");
                command.Parameters.Values.Should().BeEquivalentTo(new object[] { artifactId1, artifactId2 });
            });
        var sut = CreateSut(connection);

        var result = await sut.GetByArtifactIdsAsync(new[] { artifactId1, artifactId2 });

        result.Should().HaveCount(2);
        result.Select(c => c.IndexedArtifactId).Should().BeEquivalentTo(new[] { artifactId1, artifactId2 });
    }

    [Trait("Category", "GetByArtifactIdsAsync")]
    [Fact]
    public async Task GetByArtifactIdsAsync_ConnectionFailure_WrapsInInvalidOperationException()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetByArtifactIdsAsync(new[] { Guid.NewGuid() });

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Failed to get indexed chunks");
        exception.Which.InnerException.Should().Be(expected);
    }

    // ─────────────────────────────────────────────────────────────────────
    // GetByIngestionJobIdAsync
    // ─────────────────────────────────────────────────────────────────────

    [Trait("Category", "GetByIngestionJobIdAsync")]
    [Fact]
    public async Task GetByIngestionJobIdAsync_RowsExist_MapsResults()
    {
        var ingestionJobId = Guid.NewGuid();
        var connection = new FakeDbConnection();
        using var reader = RepositoryTestReader.CreateReader(CreateChunkRow(ingestionJobId: ingestionJobId));
        connection.EnqueueReader(
            reader,
            command =>
            {
                command.CommandText.Should().Contain("WHERE [IngestionJobId] = @IngestionJobId");
                command.Parameters["IngestionJobId"].Should().Be(ingestionJobId);
            });
        var sut = CreateSut(connection);

        var result = await sut.GetByIngestionJobIdAsync(ingestionJobId);

        result.Should().ContainSingle();
        result[0].IngestionJobId.Should().Be(ingestionJobId);
    }

    [Trait("Category", "GetByIngestionJobIdAsync")]
    [Fact]
    public async Task GetByIngestionJobIdAsync_NoRows_ReturnsEmptyList()
    {
        var connection = new FakeDbConnection();
        using var reader = RepositoryTestReader.CreateReader();
        connection.EnqueueReader(reader);
        var sut = CreateSut(connection);

        var result = await sut.GetByIngestionJobIdAsync(Guid.NewGuid());

        result.Should().BeEmpty();
    }

    [Trait("Category", "GetByIngestionJobIdAsync")]
    [Fact]
    public async Task GetByIngestionJobIdAsync_ConnectionFailure_WrapsInInvalidOperationException()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetByIngestionJobIdAsync(Guid.NewGuid());

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Failed to get indexed chunks");
        exception.Which.InnerException.Should().Be(expected);
    }

    // ─────────────────────────────────────────────────────────────────────
    // GetByUploadIdAsync
    // ─────────────────────────────────────────────────────────────────────

    [Trait("Category", "GetByUploadIdAsync")]
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task GetByUploadIdAsync_BlankUploadId_ThrowsArgumentException(string? uploadId)
    {
        var sut = CreateSut();

        var act = async () => await sut.GetByUploadIdAsync(uploadId!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName(nameof(uploadId));
    }

    [Trait("Category", "GetByUploadIdAsync")]
    [Fact]
    public async Task GetByUploadIdAsync_RowsExist_MapsResults()
    {
        var connection = new FakeDbConnection();
        using var reader = RepositoryTestReader.CreateReader(CreateChunkRow(uploadId: "upload-99"));
        connection.EnqueueReader(
            reader,
            command =>
            {
                command.CommandText.Should().Contain("WHERE [UploadId] = @UploadId");
                command.Parameters["UploadId"].Should().Be("upload-99");
            });
        var sut = CreateSut(connection);

        var result = await sut.GetByUploadIdAsync("upload-99");

        result.Should().ContainSingle();
        result[0].UploadId.Should().Be("upload-99");
    }

    [Trait("Category", "GetByUploadIdAsync")]
    [Fact]
    public async Task GetByUploadIdAsync_NoRows_ReturnsEmptyList()
    {
        var connection = new FakeDbConnection();
        using var reader = RepositoryTestReader.CreateReader();
        connection.EnqueueReader(reader);
        var sut = CreateSut(connection);

        var result = await sut.GetByUploadIdAsync("upload-99");

        result.Should().BeEmpty();
    }

    [Trait("Category", "GetByUploadIdAsync")]
    [Fact]
    public async Task GetByUploadIdAsync_ConnectionFailure_WrapsInInvalidOperationException()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetByUploadIdAsync("upload-99");

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Failed to get indexed chunks");
        exception.Which.InnerException.Should().Be(expected);
    }

    // ─────────────────────────────────────────────────────────────────────
    // DeleteByArtifactIdAsync
    // ─────────────────────────────────────────────────────────────────────

    [Trait("Category", "DeleteByArtifactIdAsync")]
    [Fact]
    public async Task DeleteByArtifactIdAsync_HappyPath_ExecutesDeleteWithTimeout()
    {
        var artifactId = Guid.NewGuid();
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(4, command =>
        {
            command.CommandText.Should().Contain("DELETE FROM [dbo].[IndexedChunks] WHERE [IndexedArtifactId] = @IndexedArtifactId");
            command.Parameters["IndexedArtifactId"].Should().Be(artifactId);
            command.CommandTimeout.Should().Be(90);
        });
        var sut = CreateSut(connection);

        await sut.DeleteByArtifactIdAsync(artifactId);

        connection.ExecutedCommands.Should().ContainSingle();
    }

    [Trait("Category", "DeleteByArtifactIdAsync")]
    [Fact]
    public async Task DeleteByArtifactIdAsync_ConnectionFailure_WrapsInInvalidOperationException()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.DeleteByArtifactIdAsync(Guid.NewGuid());

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Failed to delete indexed chunks");
        exception.Which.InnerException.Should().Be(expected);
    }

    // ─────────────────────────────────────────────────────────────────────
    // DeleteByArtifactIdsAsync
    // ─────────────────────────────────────────────────────────────────────

    [Trait("Category", "DeleteByArtifactIdsAsync")]
    [Fact]
    public async Task DeleteByArtifactIdsAsync_NullCollection_ThrowsArgumentNullException()
    {
        var sut = CreateSut();

        var act = async () => await sut.DeleteByArtifactIdsAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("artifactIds");
    }

    [Trait("Category", "DeleteByArtifactIdsAsync")]
    [Fact]
    public async Task DeleteByArtifactIdsAsync_EmptyCollection_ReturnsZeroWithoutTouchingConnection()
    {
        var factory = new Mock<ISqlConnectionFactory>();
        var sut = new IndexedChunkRepository(factory.Object, TestHelpers.CreateNullLogger<IndexedChunkRepository>());

        var result = await sut.DeleteByArtifactIdsAsync(Array.Empty<Guid>());

        result.Should().Be(0);
        factory.Verify(x => x.CreateOpenConnectionAsync(), Times.Never);
    }

    [Trait("Category", "DeleteByArtifactIdsAsync")]
    [Fact]
    public async Task DeleteByArtifactIdsAsync_HappyPath_ReturnsAffectedRowCount()
    {
        var artifactId1 = Guid.NewGuid();
        var artifactId2 = Guid.NewGuid();
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(7, command =>
        {
            command.CommandText.Should().Contain("DELETE FROM [dbo].[IndexedChunks] WHERE [IndexedArtifactId] IN (");
            command.Parameters.Values.Should().BeEquivalentTo(new object[] { artifactId1, artifactId2 });
            command.CommandTimeout.Should().Be(90);
        });
        var sut = CreateSut(connection);

        var result = await sut.DeleteByArtifactIdsAsync(new[] { artifactId1, artifactId2 });

        result.Should().Be(7);
    }

    [Trait("Category", "DeleteByArtifactIdsAsync")]
    [Fact]
    public async Task DeleteByArtifactIdsAsync_ConnectionFailure_WrapsInInvalidOperationException()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.DeleteByArtifactIdsAsync(new[] { Guid.NewGuid() });

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Failed to delete indexed chunks");
        exception.Which.InnerException.Should().Be(expected);
    }

    // ─────────────────────────────────────────────────────────────────────
    // DeleteByIngestionJobIdAsync
    // ─────────────────────────────────────────────────────────────────────

    [Trait("Category", "DeleteByIngestionJobIdAsync")]
    [Fact]
    public async Task DeleteByIngestionJobIdAsync_HappyPath_ExecutesDeleteWithTimeout()
    {
        var ingestionJobId = Guid.NewGuid();
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(2, command =>
        {
            command.CommandText.Should().Contain("DELETE FROM [dbo].[IndexedChunks] WHERE [IngestionJobId] = @IngestionJobId");
            command.Parameters["IngestionJobId"].Should().Be(ingestionJobId);
            command.CommandTimeout.Should().Be(90);
        });
        var sut = CreateSut(connection);

        await sut.DeleteByIngestionJobIdAsync(ingestionJobId);

        connection.ExecutedCommands.Should().ContainSingle();
    }

    [Trait("Category", "DeleteByIngestionJobIdAsync")]
    [Fact]
    public async Task DeleteByIngestionJobIdAsync_ConnectionFailure_WrapsInInvalidOperationException()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.DeleteByIngestionJobIdAsync(Guid.NewGuid());

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Failed to delete indexed chunks");
        exception.Which.InnerException.Should().Be(expected);
    }

    // ─────────────────────────────────────────────────────────────────────
    // DeleteByUploadIdAsync
    // ─────────────────────────────────────────────────────────────────────

    [Trait("Category", "DeleteByUploadIdAsync")]
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task DeleteByUploadIdAsync_BlankUploadId_ThrowsArgumentException(string? uploadId)
    {
        var sut = CreateSut();

        var act = async () => await sut.DeleteByUploadIdAsync(uploadId!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName(nameof(uploadId));
    }

    [Trait("Category", "DeleteByUploadIdAsync")]
    [Fact]
    public async Task DeleteByUploadIdAsync_HappyPath_ExecutesDeleteWithTimeout()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(3, command =>
        {
            command.CommandText.Should().Contain("DELETE FROM [dbo].[IndexedChunks] WHERE [UploadId] = @UploadId");
            command.Parameters["UploadId"].Should().Be("upload-77");
            command.CommandTimeout.Should().Be(90);
        });
        var sut = CreateSut(connection);

        await sut.DeleteByUploadIdAsync("upload-77");

        connection.ExecutedCommands.Should().ContainSingle();
    }

    [Trait("Category", "DeleteByUploadIdAsync")]
    [Fact]
    public async Task DeleteByUploadIdAsync_ConnectionFailure_WrapsInInvalidOperationException()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.DeleteByUploadIdAsync("upload-77");

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Failed to delete indexed chunks");
        exception.Which.InnerException.Should().Be(expected);
    }

    // ─────────────────────────────────────────────────────────────────────
    // CountByArtifactIdAsync
    // ─────────────────────────────────────────────────────────────────────

    [Trait("Category", "CountByArtifactIdAsync")]
    [Fact]
    public async Task CountByArtifactIdAsync_HappyPath_ReturnsScalarCount()
    {
        var artifactId = Guid.NewGuid();
        var connection = new FakeDbConnection();
        connection.EnqueueScalar(12, command =>
        {
            command.CommandText.Should().Contain("SELECT COUNT(*) FROM [dbo].[IndexedChunks] WHERE [IndexedArtifactId] = @IndexedArtifactId");
            command.Parameters["IndexedArtifactId"].Should().Be(artifactId);
        });
        var sut = CreateSut(connection);

        var result = await sut.CountByArtifactIdAsync(artifactId);

        result.Should().Be(12);
    }

    [Trait("Category", "CountByArtifactIdAsync")]
    [Fact]
    public async Task CountByArtifactIdAsync_ConnectionFailure_WrapsInInvalidOperationException()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.CountByArtifactIdAsync(Guid.NewGuid());

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Failed to count indexed chunks");
        exception.Which.InnerException.Should().Be(expected);
    }

    // ─────────────────────────────────────────────────────────────────────
    // CountByStatusAsync
    // ─────────────────────────────────────────────────────────────────────

    [Trait("Category", "CountByStatusAsync")]
    [Theory]
    [InlineData(ChunkIndexStatus.InProc)]
    [InlineData(ChunkIndexStatus.Complete)]
    [InlineData(ChunkIndexStatus.Failed)]
    public async Task CountByStatusAsync_HappyPath_ReturnsScalarCountForEachStatus(ChunkIndexStatus status)
    {
        var artifactId = Guid.NewGuid();
        var connection = new FakeDbConnection();
        connection.EnqueueScalar(5, command =>
        {
            command.CommandText.Should().Contain("AND [Status] = @Status");
            command.Parameters["IndexedArtifactId"].Should().Be(artifactId);
            command.Parameters["Status"].Should().Be(status.ToString());
        });
        var sut = CreateSut(connection);

        var result = await sut.CountByStatusAsync(artifactId, status.ToString());

        result.Should().Be(5);
    }

    [Trait("Category", "CountByStatusAsync")]
    [Fact]
    public async Task CountByStatusAsync_ConnectionFailure_WrapsInInvalidOperationException()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.CountByStatusAsync(Guid.NewGuid(), ChunkIndexStatus.Complete.ToString());

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Failed to count indexed chunks");
        exception.Which.InnerException.Should().Be(expected);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Test helpers
    // ─────────────────────────────────────────────────────────────────────

    private static IndexedChunkRepository CreateSut(FakeDbConnection? connection = null)
    {
        var factory = new Mock<ISqlConnectionFactory>();
        factory
            .Setup(x => x.CreateOpenConnectionAsync())
            .ReturnsAsync(connection ?? new FakeDbConnection());
        return new IndexedChunkRepository(factory.Object, TestHelpers.CreateNullLogger<IndexedChunkRepository>());
    }

    private static IndexedChunkRepository CreateThrowingSut(Exception exception)
    {
        var factory = new Mock<ISqlConnectionFactory>();
        factory.Setup(x => x.CreateOpenConnectionAsync()).ThrowsAsync(exception);
        return new IndexedChunkRepository(factory.Object, TestHelpers.CreateNullLogger<IndexedChunkRepository>());
    }

    private static IndexedChunk CreateChunk() => new()
    {
        ChunkId = Guid.NewGuid().ToString("D"),
        IndexedArtifactId = Guid.NewGuid(),
        IngestionJobId = Guid.NewGuid(),
        UploadId = "upload-1",
        SourceFileName = "manual.pdf",
        PageNumber = 1,
        ChunkIndex = 0,
        Stage = "chunking",
        Status = ChunkIndexStatus.InProc,
        // Non-null so parameter-equality assertions don't have to special-case
        // Dapper's null -> DBNull.Value translation.
        ProcessedAtUtc = new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero),
        FailureReason = "transient failure"
    };

    private static Dictionary<string, object?> CreateChunkRow(
        string? chunkId = null,
        Guid? artifactId = null,
        Guid? ingestionJobId = null,
        string? uploadId = null,
        string? sourceFileName = "manual.pdf",
        int? pageNumber = 3,
        int? chunkIndex = 5,
        string? stage = "embedding",
        ChunkIndexStatus status = ChunkIndexStatus.Complete,
        DateTimeOffset? processedAtUtc = null,
        string? failureReason = null) =>
        new()
        {
            ["ChunkId"] = chunkId ?? Guid.NewGuid().ToString("D"),
            ["IndexedArtifactId"] = artifactId ?? Guid.NewGuid(),
            ["IngestionJobId"] = ingestionJobId ?? Guid.NewGuid(),
            ["UploadId"] = uploadId ?? "upload-1",
            ["SourceFileName"] = sourceFileName,
            ["PageNumber"] = pageNumber,
            ["ChunkIndex"] = chunkIndex,
            ["Stage"] = stage,
            ["Status"] = status.ToString(),
            ["ProcessedAtUtc"] = processedAtUtc,
            ["FailureReason"] = failureReason
        };
}

