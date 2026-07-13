using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Persistence.Sql;
using MotorcycleRAG.Persistence.Sql.Repositories;

namespace MotorcycleRAG.UnitTests.Persistence.Sql.Repositories;

public sealed class ToolConfigurationRepositoryTests
{
    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenConnectionFactoryIsNull()
    {
        var act = () => new ToolConfigurationRepository(null!, NullLogger<ToolConfigurationRepository>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        var factory = new Mock<ISqlConnectionFactory>();

        var act = () => new ToolConfigurationRepository(factory.Object, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ── AddOrUpdateAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task AddOrUpdateAsync_ShouldThrowArgumentNullException_WhenConfigurationIsNull()
    {
        var sut = CreateSut();

        var act = async () => await sut.AddOrUpdateAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("configuration");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task AddOrUpdateAsync_ShouldThrowArgumentException_WhenToolIdIsBlank(string? toolId)
    {
        var sut = CreateSut();
        var configuration = CreateConfiguration(toolId: toolId!);

        var act = async () => await sut.AddOrUpdateAsync(configuration);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("configuration");
    }

    [Fact]
    public async Task AddOrUpdateAsync_ShouldAssignNewIdBeforeAttemptingPersistence_WhenIdIsEmpty()
    {
        var configuration = CreateConfiguration();
        configuration.Id = Guid.Empty;
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(1);
        var sut = CreateSut(connection);

        // The Id-generation branch runs unconditionally before the SQL is attempted,
        // so it is observable even though the call ultimately fails because Dapper has
        // no Uri type handler for ServerUrl.
        var act = async () => await sut.AddOrUpdateAsync(configuration);

        await act.Should().ThrowAsync<InvalidOperationException>();
        configuration.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task AddOrUpdateAsync_ShouldWrapUnderlyingFailure_BecauseDapperHasNoUriTypeHandlerForServerUrl()
    {
        // Known production defect: MotorcycleRAG.Persistence never registers a Dapper
        // SqlMapper.ITypeHandler for System.Uri, so binding McpToolConfiguration.ServerUrl
        // (a Uri) as an anonymous-object query parameter throws NotSupportedException
        // inside Dapper's parameter-info generator, before the command ever reaches
        // the connection. AddOrUpdateAsync's catch-all wraps this as InvalidOperationException.
        var configuration = CreateConfiguration();
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(1);
        var sut = CreateSut(connection);

        var act = async () => await sut.AddOrUpdateAsync(configuration);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain(configuration.ToolId);
        exception.Which.InnerException.Should().BeOfType<NotSupportedException>();
        exception.Which.InnerException!.Message.Should().Contain("ServerUrl");
        connection.ExecutedCommands.Should().BeEmpty(
            "Dapper throws while building the parameter binder, before any command reaches the connection");
    }

    // ── GetByIdAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_ShouldThrowArgumentException_WhenIdIsEmpty()
    {
        var sut = CreateSut();

        var act = async () => await sut.GetByIdAsync(Guid.Empty);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("id");
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnMappedConfiguration_WhenFound()
    {
        var id = Guid.NewGuid();
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            RepositoryTestReader.CreateReader(CreateConfigurationRow(id: id, toolId: "search-tool")),
            command => command.Parameters["Id"].Should().Be(id));
        var sut = CreateSut(connection);

        var result = await sut.GetByIdAsync(id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(id);
        result.ToolId.Should().Be("search-tool");
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnNull_WhenNotFound()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(RepositoryTestReader.CreateReader());
        var sut = CreateSut(connection);

        var result = await sut.GetByIdAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_ShouldWrapDatabaseFailures()
    {
        var connection = new FakeDbConnection();
        var expected = new DataException("read failed");
        connection.EnqueueReaderException(expected);
        var sut = CreateSut(connection);

        var act = async () => await sut.GetByIdAsync(Guid.NewGuid());

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.InnerException.Should().Be(expected);
    }

    // ── GetByToolIdAsync ─────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task GetByToolIdAsync_ShouldReturnNull_WhenToolIdIsBlank(string? toolId)
    {
        var connection = new FakeDbConnection();
        var sut = CreateSut(connection);

        var result = await sut.GetByToolIdAsync(toolId!);

        result.Should().BeNull();
        connection.ExecutedCommands.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByToolIdAsync_ShouldReturnMappedConfiguration_WhenFound()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            RepositoryTestReader.CreateReader(CreateConfigurationRow(toolId: "graph-tool")),
            command => command.Parameters["ToolId"].Should().Be("graph-tool"));
        var sut = CreateSut(connection);

        var result = await sut.GetByToolIdAsync("graph-tool");

        result.Should().NotBeNull();
        result!.ToolId.Should().Be("graph-tool");
    }

    [Fact]
    public async Task GetByToolIdAsync_ShouldWrapDatabaseFailures()
    {
        var connection = new FakeDbConnection();
        var expected = new DataException("read failed");
        connection.EnqueueReaderException(expected);
        var sut = CreateSut(connection);

        var act = async () => await sut.GetByToolIdAsync("graph-tool");

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.InnerException.Should().Be(expected);
    }

    // ── GetAllAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_ShouldReturnMappedConfigurations()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            RepositoryTestReader.CreateReader(
                CreateConfigurationRow(toolId: "tool-a"),
                CreateConfigurationRow(toolId: "tool-b")));
        var sut = CreateSut(connection);

        var results = await sut.GetAllAsync();

        results.Should().HaveCount(2);
        results.Select(c => c.ToolId).Should().Contain(["tool-a", "tool-b"]);
    }

    [Fact]
    public async Task GetAllAsync_ShouldReturnEmptyArray_WhenNoRows()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(RepositoryTestReader.CreateReaderWithSchema("Id"));
        var sut = CreateSut(connection);

        var results = await sut.GetAllAsync();

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllAsync_ShouldWrapDatabaseFailures()
    {
        var connection = new FakeDbConnection();
        var expected = new DataException("read failed");
        connection.EnqueueReaderException(expected);
        var sut = CreateSut(connection);

        var act = async () => await sut.GetAllAsync();

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.InnerException.Should().Be(expected);
    }

    // ── GetEnabledAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetEnabledAsync_ShouldReturnMappedConfigurations()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            RepositoryTestReader.CreateReader(CreateConfigurationRow(toolId: "enabled-tool", isEnabled: true)),
            command => command.CommandText.Should().Contain("WHERE [IsEnabled] = 1"));
        var sut = CreateSut(connection);

        var results = await sut.GetEnabledAsync();

        results.Should().ContainSingle();
        results[0].ToolId.Should().Be("enabled-tool");
    }

    [Fact]
    public async Task GetEnabledAsync_ShouldWrapDatabaseFailures()
    {
        var connection = new FakeDbConnection();
        var expected = new DataException("read failed");
        connection.EnqueueReaderException(expected);
        var sut = CreateSut(connection);

        var act = async () => await sut.GetEnabledAsync();

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.InnerException.Should().Be(expected);
    }

    // ── GetByTypeAsync ───────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task GetByTypeAsync_ShouldReturnEmptyArray_WhenToolTypeIsBlank(string? toolType)
    {
        var connection = new FakeDbConnection();
        var sut = CreateSut(connection);

        var results = await sut.GetByTypeAsync(toolType!);

        results.Should().BeEmpty();
        connection.ExecutedCommands.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByTypeAsync_ShouldReturnMappedConfigurations()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            RepositoryTestReader.CreateReader(CreateConfigurationRow(toolId: "search-tool", toolType: "search")),
            command => command.Parameters["ToolType"].Should().Be("search"));
        var sut = CreateSut(connection);

        var results = await sut.GetByTypeAsync("search");

        results.Should().ContainSingle();
        results[0].ToolType.Should().Be("search");
    }

    [Fact]
    public async Task GetByTypeAsync_ShouldWrapDatabaseFailures()
    {
        var connection = new FakeDbConnection();
        var expected = new DataException("read failed");
        connection.EnqueueReaderException(expected);
        var sut = CreateSut(connection);

        var act = async () => await sut.GetByTypeAsync("search");

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.InnerException.Should().Be(expected);
    }

    // ── DeleteAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_ShouldThrowArgumentException_WhenIdIsEmpty()
    {
        var sut = CreateSut();

        var act = async () => await sut.DeleteAsync(Guid.Empty);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("id");
    }

    [Fact]
    public async Task DeleteAsync_ShouldReturnTrue_WhenRowsAffected()
    {
        var id = Guid.NewGuid();
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(1, command => command.Parameters["Id"].Should().Be(id));
        var sut = CreateSut(connection);

        var result = await sut.DeleteAsync(id);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_ShouldReturnFalse_WhenNoRowsAffected()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(0);
        var sut = CreateSut(connection);

        var result = await sut.DeleteAsync(Guid.NewGuid());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_ShouldWrapDatabaseFailures()
    {
        var connection = new FakeDbConnection();
        var expected = new DataException("delete failed");
        connection.EnqueueNonQueryException(expected);
        var sut = CreateSut(connection);

        var act = async () => await sut.DeleteAsync(Guid.NewGuid());

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.InnerException.Should().Be(expected);
    }

    // ── ExistsAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task ExistsAsync_ShouldReturnFalse_WhenIdIsEmpty()
    {
        var connection = new FakeDbConnection();
        var sut = CreateSut(connection);

        var result = await sut.ExistsAsync(Guid.Empty);

        result.Should().BeFalse();
        connection.ExecutedCommands.Should().BeEmpty();
    }

    [Fact]
    public async Task ExistsAsync_ShouldReturnTrue_WhenCountGreaterThanZero()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(RepositoryTestReader.CreateReader(new Dictionary<string, object?> { ["Count"] = 1 }));
        var sut = CreateSut(connection);

        var result = await sut.ExistsAsync(Guid.NewGuid());

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_ShouldReturnFalse_WhenCountIsZero()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(RepositoryTestReader.CreateReader(new Dictionary<string, object?> { ["Count"] = 0 }));
        var sut = CreateSut(connection);

        var result = await sut.ExistsAsync(Guid.NewGuid());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsync_ShouldWrapDatabaseFailures()
    {
        var connection = new FakeDbConnection();
        var expected = new DataException("query failed");
        connection.EnqueueReaderException(expected);
        var sut = CreateSut(connection);

        var act = async () => await sut.ExistsAsync(Guid.NewGuid());

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.InnerException.Should().Be(expected);
    }

    // ── ExistsByToolIdAsync ──────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task ExistsByToolIdAsync_ShouldReturnFalse_WhenToolIdIsBlank(string? toolId)
    {
        var connection = new FakeDbConnection();
        var sut = CreateSut(connection);

        var result = await sut.ExistsByToolIdAsync(toolId!);

        result.Should().BeFalse();
        connection.ExecutedCommands.Should().BeEmpty();
    }

    [Fact]
    public async Task ExistsByToolIdAsync_ShouldReturnTrue_WhenCountGreaterThanZero()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(RepositoryTestReader.CreateReader(new Dictionary<string, object?> { ["Count"] = 3 }));
        var sut = CreateSut(connection);

        var result = await sut.ExistsByToolIdAsync("search-tool");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsByToolIdAsync_ShouldWrapDatabaseFailures()
    {
        var connection = new FakeDbConnection();
        var expected = new DataException("query failed");
        connection.EnqueueReaderException(expected);
        var sut = CreateSut(connection);

        var act = async () => await sut.ExistsByToolIdAsync("search-tool");

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.InnerException.Should().Be(expected);
    }

    // ── CountAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task CountAsync_ShouldReturnCount()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(RepositoryTestReader.CreateReader(new Dictionary<string, object?> { ["Count"] = 7 }));
        var sut = CreateSut(connection);

        var result = await sut.CountAsync();

        result.Should().Be(7);
    }

    [Fact]
    public async Task CountAsync_ShouldWrapDatabaseFailures()
    {
        var connection = new FakeDbConnection();
        var expected = new DataException("count failed");
        connection.EnqueueReaderException(expected);
        var sut = CreateSut(connection);

        var act = async () => await sut.CountAsync();

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.InnerException.Should().Be(expected);
    }

    // ── Test helpers ─────────────────────────────────────────────────────

    private static ToolConfigurationRepository CreateSut(FakeDbConnection? connection = null)
    {
        var factory = new Mock<ISqlConnectionFactory>();
        factory
            .Setup(x => x.CreateOpenConnectionAsync())
            .ReturnsAsync(connection ?? new FakeDbConnection());
        return new ToolConfigurationRepository(factory.Object, NullLogger<ToolConfigurationRepository>.Instance);
    }

    private static McpToolConfiguration CreateConfiguration(
        Guid? id = null,
        string toolId = "search-tool",
        string toolType = "search") =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            ToolId = toolId,
            Name = "Search Tool",
            Description = "Performs vector search",
            ServerUrl = new Uri("https://tools.example.com/mcp"),
            ToolType = toolType,
            Version = "1.0",
            IsEnabled = true,
            IsSystemTool = false,
            Priority = 10,
            TimeoutMs = 30000,
            RetryOnFailure = true,
            MaxRetries = 3,
            DisabledReason = null,
            LastConnectionStatus = "Ok",
            LastTestedAt = new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc),
            ConfigurationJson = "{}",
            CreatedAt = new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 7, 5, 9, 0, 0, DateTimeKind.Utc)
        };

    private static Dictionary<string, object?> CreateConfigurationRow(
        Guid? id = null,
        string toolId = "search-tool",
        string toolType = "search",
        bool isEnabled = true) =>
        new()
        {
            ["Id"] = id ?? Guid.NewGuid(),
            ["ToolId"] = toolId,
            ["Name"] = "Search Tool",
            ["Description"] = "Performs vector search",
            ["ServerUrl"] = new Uri("https://tools.example.com/mcp"),
            ["ToolType"] = toolType,
            ["Version"] = "1.0",
            ["IsEnabled"] = isEnabled,
            ["IsSystemTool"] = false,
            ["Priority"] = 10,
            ["TimeoutMs"] = 30000,
            ["RetryOnFailure"] = true,
            ["MaxRetries"] = 3,
            ["DisabledReason"] = null,
            ["LastConnectionStatus"] = "Ok",
            ["LastTestedAt"] = new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc),
            ["ConfigurationJson"] = "{}",
            ["CreatedAt"] = new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc),
            ["UpdatedAt"] = new DateTime(2026, 7, 5, 9, 0, 0, DateTimeKind.Utc)
        };
}
