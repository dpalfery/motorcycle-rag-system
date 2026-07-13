using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;
using MotorcycleRAG.Persistence.Sql;
using MotorcycleRAG.Persistence.Sql.Repositories;

namespace MotorcycleRAG.UnitTests.Persistence.Sql.Repositories;

public sealed class IndexedArtifactRepositoryTests
{
    // ─────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────

    [Trait("Category", "Constructor")]
    [Fact]
    public void Constructor_NullConnectionFactory_ThrowsArgumentNullException()
    {
        var act = () => new IndexedArtifactRepository(null!, TestHelpers.CreateNullLogger<IndexedArtifactRepository>());

        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Trait("Category", "Constructor")]
    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var factory = new Mock<ISqlConnectionFactory>();

        var act = () => new IndexedArtifactRepository(factory.Object, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ─────────────────────────────────────────────────────────────────────
    // UpsertAsync
    // ─────────────────────────────────────────────────────────────────────

    [Trait("Category", "UpsertAsync")]
    [Fact]
    public async Task UpsertAsync_NullArtifact_ThrowsArgumentNullException()
    {
        var sut = CreateSut();

        var act = async () => await sut.UpsertAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("artifact");
    }

    [Trait("Category", "UpsertAsync")]
    [Fact]
    public async Task UpsertAsync_HappyPath_ExecutesMergeWithExpectedParametersAndReturnsSameInstance()
    {
        var artifact = CreateArtifact();
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(1, command =>
        {
            command.CommandText.Should().Contain("MERGE [dbo].[IndexedArtifacts]");
            command.Parameters["IndexedArtifactId"].Should().Be(artifact.IndexedArtifactId);
            command.Parameters["IngestionJobId"].Should().Be(artifact.IngestionJobId);
            command.Parameters["UploadId"].Should().Be(artifact.UploadId);
            command.Parameters["ArtifactType"].Should().Be(artifact.ArtifactType);
            command.Parameters["BlobContainer"].Should().Be(artifact.BlobContainer);
            command.Parameters["BlobPath"].Should().Be(artifact.BlobPath);
            command.Parameters["SourceFileName"].Should().Be(artifact.SourceFileName);
            command.Parameters["State"].Should().Be(artifact.State.ToString());
            command.Parameters["ExpectedChunkCount"].Should().Be(artifact.ExpectedChunkCount);
            command.Parameters["IndexedChunkCount"].Should().Be(artifact.IndexedChunkCount);
            command.Parameters["FailedChunkCount"].Should().Be(artifact.FailedChunkCount);
            command.Parameters["LastProcessedAtUtc"].Should().Be(artifact.LastProcessedAtUtc);
            command.Parameters["FailureReason"].Should().Be(artifact.FailureReason);
        });
        var sut = CreateSut(connection);

        var result = await sut.UpsertAsync(artifact);

        result.Should().BeSameAs(artifact);
        connection.ExecutedCommands.Should().ContainSingle();
    }

    [Trait("Category", "UpsertAsync")]
    [Fact]
    public async Task UpsertAsync_ConnectionFailure_WrapsInInvalidOperationException()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.UpsertAsync(CreateArtifact());

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Failed to upsert indexed artifact");
        exception.Which.InnerException.Should().Be(expected);
    }

    [Trait("Category", "UpsertAsync")]
    [Fact]
    public async Task UpsertAsync_ExecuteFailure_WrapsInInvalidOperationException()
    {
        var expected = new InvalidOperationException("execute boom");
        var connection = new FakeDbConnection();
        connection.EnqueueNonQueryException(expected);
        var sut = CreateSut(connection);

        var act = async () => await sut.UpsertAsync(CreateArtifact());

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.InnerException.Should().Be(expected);
    }

    // ─────────────────────────────────────────────────────────────────────
    // GetByIdAsync
    // ─────────────────────────────────────────────────────────────────────

    [Trait("Category", "GetByIdAsync")]
    [Theory]
    [InlineData(IndexedArtifactState.Pending)]
    [InlineData(IndexedArtifactState.Indexing)]
    [InlineData(IndexedArtifactState.Completed)]
    [InlineData(IndexedArtifactState.PartiallyIndexed)]
    [InlineData(IndexedArtifactState.Failed)]
    public async Task GetByIdAsync_RowExists_MapsAllStates(IndexedArtifactState state)
    {
        var artifactId = Guid.NewGuid();
        var connection = new FakeDbConnection();
        using var reader = RepositoryTestReader.CreateReader(CreateArtifactRow(artifactId: artifactId, state: state));
        connection.EnqueueReader(
            reader,
            command =>
            {
                command.CommandText.Should().Contain("WHERE [IndexedArtifactId] = @IndexedArtifactId");
                command.Parameters["IndexedArtifactId"].Should().Be(artifactId);
            });
        var sut = CreateSut(connection);

        var result = await sut.GetByIdAsync(artifactId);

        result.Should().NotBeNull();
        result!.State.Should().Be(state);
        result.IndexedArtifactId.Should().Be(artifactId);
    }

    [Trait("Category", "GetByIdAsync")]
    [Fact]
    public async Task GetByIdAsync_RowExists_MapsAllFields()
    {
        var artifactId = Guid.NewGuid();
        var ingestionJobId = Guid.NewGuid();
        var lastProcessedAt = new DateTimeOffset(2026, 7, 11, 8, 30, 0, TimeSpan.Zero);
        var createdAt = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var updatedAt = new DateTimeOffset(2026, 7, 2, 9, 0, 0, TimeSpan.Zero);
        var connection = new FakeDbConnection();
        using var reader = RepositoryTestReader.CreateReader(CreateArtifactRow(
            artifactId: artifactId,
            ingestionJobId: ingestionJobId,
            uploadId: "upload-1",
            artifactType: "Manual",
            blobContainer: "manuals",
            blobPath: "honda/cbr600.pdf",
            sourceFileName: "cbr600.pdf",
            state: IndexedArtifactState.PartiallyIndexed,
            expectedChunkCount: 10,
            indexedChunkCount: 7,
            failedChunkCount: 1,
            lastProcessedAtUtc: lastProcessedAt,
            failureReason: "partial timeout",
            createdAtUtc: createdAt,
            updatedAtUtc: updatedAt));
        connection.EnqueueReader(reader);
        var sut = CreateSut(connection);

        var result = await sut.GetByIdAsync(artifactId);

        result.Should().NotBeNull();
        result!.IndexedArtifactId.Should().Be(artifactId);
        result.IngestionJobId.Should().Be(ingestionJobId);
        result.UploadId.Should().Be("upload-1");
        result.ArtifactType.Should().Be("Manual");
        result.BlobContainer.Should().Be("manuals");
        result.BlobPath.Should().Be("honda/cbr600.pdf");
        result.SourceFileName.Should().Be("cbr600.pdf");
        result.State.Should().Be(IndexedArtifactState.PartiallyIndexed);
        result.ExpectedChunkCount.Should().Be(10);
        result.IndexedChunkCount.Should().Be(7);
        result.FailedChunkCount.Should().Be(1);
        result.LastProcessedAtUtc.Should().Be(lastProcessedAt);
        result.FailureReason.Should().Be("partial timeout");
        result.CreatedAtUtc.Should().Be(createdAt);
        result.UpdatedAtUtc.Should().Be(updatedAt);
    }

    [Trait("Category", "GetByIdAsync")]
    [Fact]
    public async Task GetByIdAsync_NoRow_ReturnsNull()
    {
        var connection = new FakeDbConnection();
        using var reader = RepositoryTestReader.CreateReader();
        connection.EnqueueReader(reader);
        var sut = CreateSut(connection);

        var result = await sut.GetByIdAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Trait("Category", "GetByIdAsync")]
    [Fact]
    public async Task GetByIdAsync_ConnectionFailure_WrapsInInvalidOperationException()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetByIdAsync(Guid.NewGuid());

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Failed to get indexed artifact");
        exception.Which.InnerException.Should().Be(expected);
    }

    // ─────────────────────────────────────────────────────────────────────
    // GetByUploadAndTypeAsync
    // ─────────────────────────────────────────────────────────────────────

    [Trait("Category", "GetByUploadAndTypeAsync")]
    [Fact]
    public async Task GetByUploadAndTypeAsync_RowExists_MapsResult()
    {
        var connection = new FakeDbConnection();
        using var reader = RepositoryTestReader.CreateReader(CreateArtifactRow(uploadId: "upload-5", artifactType: "Csv"));
        connection.EnqueueReader(
            reader,
            command =>
            {
                command.CommandText.Should().Contain("WHERE [UploadId] = @UploadId AND [ArtifactType] = @ArtifactType");
                command.Parameters["UploadId"].Should().Be("upload-5");
                command.Parameters["ArtifactType"].Should().Be("Csv");
            });
        var sut = CreateSut(connection);

        var result = await sut.GetByUploadAndTypeAsync("upload-5", "Csv");

        result.Should().NotBeNull();
        result!.UploadId.Should().Be("upload-5");
        result.ArtifactType.Should().Be("Csv");
    }

    [Trait("Category", "GetByUploadAndTypeAsync")]
    [Fact]
    public async Task GetByUploadAndTypeAsync_NoRow_ReturnsNull()
    {
        var connection = new FakeDbConnection();
        using var reader = RepositoryTestReader.CreateReader();
        connection.EnqueueReader(reader);
        var sut = CreateSut(connection);

        var result = await sut.GetByUploadAndTypeAsync("upload-5", "Csv");

        result.Should().BeNull();
    }

    [Trait("Category", "GetByUploadAndTypeAsync")]
    [Fact]
    public async Task GetByUploadAndTypeAsync_ConnectionFailure_WrapsInInvalidOperationException()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetByUploadAndTypeAsync("upload-5", "Csv");

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Failed to get indexed artifact");
        exception.Which.InnerException.Should().Be(expected);
    }

    // ─────────────────────────────────────────────────────────────────────
    // GetByStatesAsync
    // ─────────────────────────────────────────────────────────────────────

    [Trait("Category", "GetByStatesAsync")]
    [Fact]
    public async Task GetByStatesAsync_EmptyCollection_ReturnsEmptyWithoutTouchingConnection()
    {
        var factory = new Mock<ISqlConnectionFactory>();
        var sut = new IndexedArtifactRepository(factory.Object, TestHelpers.CreateNullLogger<IndexedArtifactRepository>());

        var result = await sut.GetByStatesAsync(Array.Empty<IndexedArtifactState>());

        result.Should().BeEmpty();
        factory.Verify(x => x.CreateOpenConnectionAsync(), Times.Never);
    }

    [Trait("Category", "GetByStatesAsync")]
    [Fact]
    public async Task GetByStatesAsync_HappyPath_MapsResultsAndPassesStateStrings()
    {
        var connection = new FakeDbConnection();
        using var reader = RepositoryTestReader.CreateReader(
            CreateArtifactRow(state: IndexedArtifactState.Pending),
            CreateArtifactRow(state: IndexedArtifactState.Failed));
        connection.EnqueueReader(
            reader,
            command =>
            {
                command.CommandText.Should().Contain("WHERE [State] IN (");
                command.CommandText.Should().Contain("ORDER BY [CreatedAtUtc] DESC");
                command.Parameters.Values.Should().BeEquivalentTo(new object[]
                {
                    IndexedArtifactState.Pending.ToString(),
                    IndexedArtifactState.Failed.ToString()
                });
            });
        var sut = CreateSut(connection);

        var result = await sut.GetByStatesAsync(new[] { IndexedArtifactState.Pending, IndexedArtifactState.Failed });

        result.Should().HaveCount(2);
        result.Select(a => a.State).Should().BeEquivalentTo(new[] { IndexedArtifactState.Pending, IndexedArtifactState.Failed });
    }

    [Trait("Category", "GetByStatesAsync")]
    [Fact]
    public async Task GetByStatesAsync_ConnectionFailure_WrapsInInvalidOperationException()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetByStatesAsync(new[] { IndexedArtifactState.Pending });

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Failed to get indexed artifacts");
        exception.Which.InnerException.Should().Be(expected);
    }

    // ─────────────────────────────────────────────────────────────────────
    // GetAllAsync
    // ─────────────────────────────────────────────────────────────────────

    [Trait("Category", "GetAllAsync")]
    [Fact]
    public async Task GetAllAsync_DefaultMaxCount_UsesDefaultOf1000()
    {
        var connection = new FakeDbConnection();
        using var reader = RepositoryTestReader.CreateReader(CreateArtifactRow());
        connection.EnqueueReader(
            reader,
            command =>
            {
                command.CommandText.Should().Contain("TOP (@MaxCount)");
                command.CommandText.Should().Contain("ORDER BY [CreatedAtUtc] DESC");
                command.Parameters["MaxCount"].Should().Be(1000);
            });
        var sut = CreateSut(connection);

        var result = await sut.GetAllAsync();

        result.Should().ContainSingle();
    }

    [Trait("Category", "GetAllAsync")]
    [Fact]
    public async Task GetAllAsync_CustomMaxCount_PassesProvidedValue()
    {
        var connection = new FakeDbConnection();
        using var reader = RepositoryTestReader.CreateReader();
        connection.EnqueueReader(
            reader,
            command => command.Parameters["MaxCount"].Should().Be(25));
        var sut = CreateSut(connection);

        var result = await sut.GetAllAsync(25);

        result.Should().BeEmpty();
    }

    [Trait("Category", "GetAllAsync")]
    [Fact]
    public async Task GetAllAsync_ConnectionFailure_WrapsInInvalidOperationException()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetAllAsync();

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Failed to get indexed artifacts");
        exception.Which.InnerException.Should().Be(expected);
    }

    // ─────────────────────────────────────────────────────────────────────
    // GetByIngestionJobIdAsync
    // ─────────────────────────────────────────────────────────────────────

    [Trait("Category", "GetByIngestionJobIdAsync")]
    [Fact]
    public async Task GetByIngestionJobIdAsync_RowsExist_MapsResults()
    {
        var jobId = Guid.NewGuid();
        var connection = new FakeDbConnection();
        using var reader = RepositoryTestReader.CreateReader(CreateArtifactRow(ingestionJobId: jobId));
        connection.EnqueueReader(
            reader,
            command =>
            {
                command.CommandText.Should().Contain("WHERE [IngestionJobId] = @IngestionJobId");
                command.Parameters["IngestionJobId"].Should().Be(jobId);
            });
        var sut = CreateSut(connection);

        var result = await sut.GetByIngestionJobIdAsync(jobId);

        result.Should().ContainSingle();
        result[0].IngestionJobId.Should().Be(jobId);
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
        exception.Which.Message.Should().Be("Failed to get indexed artifacts");
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
        using var reader = RepositoryTestReader.CreateReader(CreateArtifactRow(uploadId: "upload-33"));
        connection.EnqueueReader(
            reader,
            command =>
            {
                command.CommandText.Should().Contain("WHERE [UploadId] = @UploadId");
                command.Parameters["UploadId"].Should().Be("upload-33");
            });
        var sut = CreateSut(connection);

        var result = await sut.GetByUploadIdAsync("upload-33");

        result.Should().ContainSingle();
        result[0].UploadId.Should().Be("upload-33");
    }

    [Trait("Category", "GetByUploadIdAsync")]
    [Fact]
    public async Task GetByUploadIdAsync_NoRows_ReturnsEmptyList()
    {
        var connection = new FakeDbConnection();
        using var reader = RepositoryTestReader.CreateReader();
        connection.EnqueueReader(reader);
        var sut = CreateSut(connection);

        var result = await sut.GetByUploadIdAsync("upload-33");

        result.Should().BeEmpty();
    }

    [Trait("Category", "GetByUploadIdAsync")]
    [Fact]
    public async Task GetByUploadIdAsync_ConnectionFailure_WrapsInInvalidOperationException()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetByUploadIdAsync("upload-33");

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Failed to get indexed artifacts");
        exception.Which.InnerException.Should().Be(expected);
    }

    // ─────────────────────────────────────────────────────────────────────
    // DeleteByIdAsync
    // ─────────────────────────────────────────────────────────────────────

    [Trait("Category", "DeleteByIdAsync")]
    [Fact]
    public async Task DeleteByIdAsync_HappyPath_ExecutesDeleteWithTimeout()
    {
        var artifactId = Guid.NewGuid();
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(1, command =>
        {
            command.CommandText.Should().Contain("DELETE FROM [dbo].[IndexedArtifacts] WHERE [IndexedArtifactId] = @IndexedArtifactId");
            command.Parameters["IndexedArtifactId"].Should().Be(artifactId);
            command.CommandTimeout.Should().Be(90);
        });
        var sut = CreateSut(connection);

        await sut.DeleteByIdAsync(artifactId);

        connection.ExecutedCommands.Should().ContainSingle();
    }

    [Trait("Category", "DeleteByIdAsync")]
    [Fact]
    public async Task DeleteByIdAsync_ConnectionFailure_WrapsInInvalidOperationException()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.DeleteByIdAsync(Guid.NewGuid());

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Failed to delete indexed artifact");
        exception.Which.InnerException.Should().Be(expected);
    }

    // ─────────────────────────────────────────────────────────────────────
    // DeleteByIdsAsync
    // ─────────────────────────────────────────────────────────────────────

    [Trait("Category", "DeleteByIdsAsync")]
    [Fact]
    public async Task DeleteByIdsAsync_NullCollection_ThrowsArgumentNullException()
    {
        var sut = CreateSut();

        var act = async () => await sut.DeleteByIdsAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("artifactIds");
    }

    [Trait("Category", "DeleteByIdsAsync")]
    [Fact]
    public async Task DeleteByIdsAsync_EmptyCollection_ReturnsZeroWithoutTouchingConnection()
    {
        var factory = new Mock<ISqlConnectionFactory>();
        var sut = new IndexedArtifactRepository(factory.Object, TestHelpers.CreateNullLogger<IndexedArtifactRepository>());

        var result = await sut.DeleteByIdsAsync(Array.Empty<Guid>());

        result.Should().Be(0);
        factory.Verify(x => x.CreateOpenConnectionAsync(), Times.Never);
    }

    [Trait("Category", "DeleteByIdsAsync")]
    [Fact]
    public async Task DeleteByIdsAsync_HappyPath_ReturnsAffectedRowCount()
    {
        var artifactId1 = Guid.NewGuid();
        var artifactId2 = Guid.NewGuid();
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(2, command =>
        {
            command.CommandText.Should().Contain("DELETE FROM [dbo].[IndexedArtifacts] WHERE [IndexedArtifactId] IN (");
            command.Parameters.Values.Should().BeEquivalentTo(new object[] { artifactId1, artifactId2 });
            command.CommandTimeout.Should().Be(90);
        });
        var sut = CreateSut(connection);

        var result = await sut.DeleteByIdsAsync(new[] { artifactId1, artifactId2 });

        result.Should().Be(2);
    }

    [Trait("Category", "DeleteByIdsAsync")]
    [Fact]
    public async Task DeleteByIdsAsync_ConnectionFailure_WrapsInInvalidOperationException()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.DeleteByIdsAsync(new[] { Guid.NewGuid() });

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Failed to delete indexed artifacts by identifiers");
        exception.Which.InnerException.Should().Be(expected);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Test helpers
    // ─────────────────────────────────────────────────────────────────────

    private static IndexedArtifactRepository CreateSut(FakeDbConnection? connection = null)
    {
        var factory = new Mock<ISqlConnectionFactory>();
        factory
            .Setup(x => x.CreateOpenConnectionAsync())
            .ReturnsAsync(connection ?? new FakeDbConnection());
        return new IndexedArtifactRepository(factory.Object, TestHelpers.CreateNullLogger<IndexedArtifactRepository>());
    }

    private static IndexedArtifactRepository CreateThrowingSut(Exception exception)
    {
        var factory = new Mock<ISqlConnectionFactory>();
        factory.Setup(x => x.CreateOpenConnectionAsync()).ThrowsAsync(exception);
        return new IndexedArtifactRepository(factory.Object, TestHelpers.CreateNullLogger<IndexedArtifactRepository>());
    }

    private static IndexedArtifact CreateArtifact() => new()
    {
        IndexedArtifactId = Guid.NewGuid(),
        IngestionJobId = Guid.NewGuid(),
        UploadId = "upload-1",
        ArtifactType = "Manual",
        BlobContainer = "manuals",
        BlobPath = "honda/cbr600.pdf",
        SourceFileName = "cbr600.pdf",
        State = IndexedArtifactState.Pending,
        ExpectedChunkCount = 10,
        IndexedChunkCount = 0,
        FailedChunkCount = 0,
        // Non-null so parameter-equality assertions don't have to special-case
        // Dapper's null -> DBNull.Value translation.
        LastProcessedAtUtc = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero),
        FailureReason = "previous transient failure",
        CreatedAtUtc = DateTimeOffset.UtcNow,
        UpdatedAtUtc = null
    };

    private static Dictionary<string, object?> CreateArtifactRow(
        Guid? artifactId = null,
        Guid? ingestionJobId = null,
        string? uploadId = null,
        string? artifactType = "Manual",
        string? blobContainer = "manuals",
        string? blobPath = "honda/cbr600.pdf",
        string? sourceFileName = "cbr600.pdf",
        IndexedArtifactState state = IndexedArtifactState.Pending,
        int? expectedChunkCount = 10,
        int? indexedChunkCount = 3,
        int? failedChunkCount = 0,
        DateTimeOffset? lastProcessedAtUtc = null,
        string? failureReason = null,
        DateTimeOffset? createdAtUtc = null,
        DateTimeOffset? updatedAtUtc = null) =>
        new()
        {
            ["IndexedArtifactId"] = artifactId ?? Guid.NewGuid(),
            ["IngestionJobId"] = ingestionJobId ?? Guid.NewGuid(),
            ["UploadId"] = uploadId ?? "upload-1",
            ["ArtifactType"] = artifactType,
            ["BlobContainer"] = blobContainer,
            ["BlobPath"] = blobPath,
            ["SourceFileName"] = sourceFileName,
            ["State"] = state.ToString(),
            ["ExpectedChunkCount"] = expectedChunkCount,
            ["IndexedChunkCount"] = indexedChunkCount,
            ["FailedChunkCount"] = failedChunkCount,
            ["LastProcessedAtUtc"] = lastProcessedAtUtc,
            ["FailureReason"] = failureReason,
            ["CreatedAtUtc"] = createdAtUtc ?? DateTimeOffset.UtcNow,
            ["UpdatedAtUtc"] = updatedAtUtc
        };
}

