using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Persistence.Sql;
using MotorcycleRAG.Persistence.Sql.Repositories;

namespace MotorcycleRAG.UnitTests.Persistence.Sql.Repositories;

public sealed class AuditRepositoryTests
{
    // ── Constructor ───────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Constructor")]
    public void Constructor_ShouldThrowArgumentNullException_WhenConnectionFactoryIsNull()
    {
        var act = () => new AuditRepository(null!, TestHelpers.CreateNullLogger<AuditRepository>());

        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Fact]
    [Trait("Category", "Constructor")]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        var factory = new Mock<ISqlConnectionFactory>();

        var act = () => new AuditRepository(factory.Object, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ── CreateAuditLogAsync ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "CreateAuditLogAsync")]
    public async Task CreateAuditLogAsync_ShouldThrowArgumentNullException_WhenAuditLogIsNull()
    {
        var sut = CreateSut();

        var act = async () => await sut.CreateAuditLogAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("auditLog");
    }

    [Fact]
    [Trait("Category", "CreateAuditLogAsync")]
    public async Task CreateAuditLogAsync_ShouldReturnAuditLogWithGeneratedId_AndCommitTransaction()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            RepositoryTestReader.CreateReader(new Dictionary<string, object?> { ["Id"] = 101L }),
            command =>
            {
                command.CommandText.Should().Contain("INSERT INTO [dbo].[AuditLogs]");
                command.HasTransaction.Should().BeTrue();
                command.Parameters["UserId"].Should().Be("user-1");
                command.Parameters["UserEmail"].Should().Be("user@example.com");
                command.Parameters["Action"].Should().Be("Create");
                command.Parameters["EntityType"].Should().Be("Order");
                command.Parameters["EntityId"].Should().Be("order-42");
                command.Parameters["Status"].Should().Be("Success");
            });
        var sut = CreateSut(connection);
        var auditLog = new AuditLog
        {
            UserId = "user-1",
            UserEmail = "user@example.com",
            Action = "Create",
            EntityType = "Order",
            EntityId = "order-42",
            Status = "Success"
        };

        var result = await sut.CreateAuditLogAsync(auditLog);

        result.Should().BeSameAs(auditLog);
        result.Id.Should().Be(101L);
        connection.BeginTransactionCount.Should().Be(1);
        connection.LastTransaction.Should().NotBeNull();
        connection.LastTransaction!.CommitCount.Should().Be(1);
        connection.LastTransaction.RollbackCount.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "CreateAuditLogAsync")]
    public async Task CreateAuditLogAsync_ShouldRollbackAndWrap_WhenInsertFails()
    {
        var expected = new InvalidOperationException("insert boom");
        var connection = new FakeDbConnection();
        connection.EnqueueReaderException(expected);
        var sut = CreateSut(connection);
        var auditLog = new AuditLog { Action = "Create", EntityType = "Order", EntityId = "order-1" };

        var act = async () => await sut.CreateAuditLogAsync(auditLog);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Failed to create audit log");
        exception.Which.InnerException.Should().BeSameAs(expected);
        connection.LastTransaction.Should().NotBeNull();
        connection.LastTransaction!.RollbackCount.Should().Be(1);
        connection.LastTransaction.CommitCount.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "CreateAuditLogAsync")]
    public async Task CreateAuditLogAsync_ShouldWrapConnectionFailures_WhenConnectionFactoryThrows()
    {
        var expected = new InvalidOperationException("connection boom");
        var sut = CreateThrowingSut(expected);
        var auditLog = new AuditLog { Action = "Create", EntityType = "Order", EntityId = "order-1" };

        var act = async () => await sut.CreateAuditLogAsync(auditLog);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Failed to create audit log");
        exception.Which.InnerException.Should().BeSameAs(expected);
    }

    // ── GetAuditLogsByEntityAsync ────────────────────────────────────────

    [Theory]
    [Trait("Category", "GetAuditLogsByEntityAsync")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task GetAuditLogsByEntityAsync_ShouldThrowArgumentException_WhenEntityTypeIsBlank(string? entityType)
    {
        var sut = CreateSut();

        var act = async () => await sut.GetAuditLogsByEntityAsync(entityType!, "order-1");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("entityType");
    }

    [Theory]
    [Trait("Category", "GetAuditLogsByEntityAsync")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task GetAuditLogsByEntityAsync_ShouldThrowArgumentException_WhenEntityIdIsBlank(string? entityId)
    {
        var sut = CreateSut();

        var act = async () => await sut.GetAuditLogsByEntityAsync("Order", entityId!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("entityId");
    }

    [Fact]
    [Trait("Category", "GetAuditLogsByEntityAsync")]
    public async Task GetAuditLogsByEntityAsync_ShouldReturnMappedRows()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            RepositoryTestReader.CreateReader(
                CreateAuditLogRow(1L, "Order", "order-1", action: "Create"),
                CreateAuditLogRow(2L, "Order", "order-1", action: "Update")),
            command =>
            {
                command.CommandText.Should().Contain("[dbo].[sp_GetAuditLogsByEntity]");
                command.Parameters["EntityType"].Should().Be("Order");
                command.Parameters["EntityId"].Should().Be("order-1");
            });
        var sut = CreateSut(connection);

        var result = await sut.GetAuditLogsByEntityAsync("Order", "order-1");

        result.Should().HaveCount(2);
        result[0].Id.Should().Be(1L);
        result[0].Action.Should().Be("Create");
        result[1].Id.Should().Be(2L);
        result[1].Action.Should().Be("Update");
        result.Should().OnlyContain(log => log.EntityType == "Order" && log.EntityId == "order-1");
    }

    [Fact]
    [Trait("Category", "GetAuditLogsByEntityAsync")]
    public async Task GetAuditLogsByEntityAsync_ShouldReturnEmptyArray_WhenNoRowsExist()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(RepositoryTestReader.CreateReaderWithSchema("Id"));
        var sut = CreateSut(connection);

        var result = await sut.GetAuditLogsByEntityAsync("Order", "order-1");

        result.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "GetAuditLogsByEntityAsync")]
    public async Task GetAuditLogsByEntityAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetAuditLogsByEntityAsync("Order", "order-1");

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Failed to get audit logs for entity Order with ID order-1");
        exception.Which.InnerException.Should().BeSameAs(expected);
    }

    // ── GetRecentAuditLogsAsync ──────────────────────────────────────────

    [Theory]
    [Trait("Category", "GetRecentAuditLogsAsync")]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task GetRecentAuditLogsAsync_ShouldThrowArgumentException_WhenLimitIsNotPositive(int limit)
    {
        var sut = CreateSut();

        var act = async () => await sut.GetRecentAuditLogsAsync(limit);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("limit");
    }

    [Fact]
    [Trait("Category", "GetRecentAuditLogsAsync")]
    public async Task GetRecentAuditLogsAsync_ShouldReturnMappedRows()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            RepositoryTestReader.CreateReader(
                CreateAuditLogRow(10L, "Order", "order-9", action: "Delete")),
            command =>
            {
                command.CommandText.Should().Contain("[dbo].[sp_GetRecentAuditLogs]");
                command.Parameters["Limit"].Should().Be(25);
            });
        var sut = CreateSut(connection);

        var result = await sut.GetRecentAuditLogsAsync(25);

        result.Should().ContainSingle();
        result[0].Id.Should().Be(10L);
        result[0].Action.Should().Be("Delete");
    }

    [Fact]
    [Trait("Category", "GetRecentAuditLogsAsync")]
    public async Task GetRecentAuditLogsAsync_ShouldReturnEmptyArray_WhenNoRowsExist()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(RepositoryTestReader.CreateReaderWithSchema("Id"));
        var sut = CreateSut(connection);

        var result = await sut.GetRecentAuditLogsAsync(10);

        result.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "GetRecentAuditLogsAsync")]
    public async Task GetRecentAuditLogsAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetRecentAuditLogsAsync(10);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Failed to get recent audit logs with limit 10");
        exception.Which.InnerException.Should().BeSameAs(expected);
    }

    // ── Test helpers ─────────────────────────────────────────────────────

    private static AuditRepository CreateSut(FakeDbConnection? connection = null)
    {
        var factory = new Mock<ISqlConnectionFactory>();
        factory
            .Setup(x => x.CreateOpenConnectionAsync())
            .ReturnsAsync(connection ?? new FakeDbConnection());
        return new AuditRepository(factory.Object, TestHelpers.CreateNullLogger<AuditRepository>());
    }

    private static AuditRepository CreateThrowingSut(Exception exception)
    {
        var factory = new Mock<ISqlConnectionFactory>();
        factory.Setup(x => x.CreateOpenConnectionAsync()).ThrowsAsync(exception);
        return new AuditRepository(factory.Object, TestHelpers.CreateNullLogger<AuditRepository>());
    }

    private static Dictionary<string, object?> CreateAuditLogRow(
        long id,
        string entityType,
        string entityId,
        string action = "Create",
        string status = "Success") =>
        new()
        {
            ["Id"] = id,
            ["UserId"] = "user-1",
            ["UserEmail"] = "user@example.com",
            ["Action"] = action,
            ["EntityType"] = entityType,
            ["EntityId"] = entityId,
            ["OldValue"] = (string?)null,
            ["NewValue"] = (string?)null,
            ["ActionDate"] = new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc),
            ["IpAddress"] = "127.0.0.1",
            ["UserAgent"] = "test-agent",
            ["Metadata"] = (string?)null,
            ["Status"] = status,
            ["ErrorMessage"] = (string?)null
        };
}

