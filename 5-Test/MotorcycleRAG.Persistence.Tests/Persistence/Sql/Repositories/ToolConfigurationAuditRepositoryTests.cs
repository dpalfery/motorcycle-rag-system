using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Persistence.Sql;
using MotorcycleRAG.Persistence.Sql.Repositories;

namespace MotorcycleRAG.UnitTests.Persistence.Sql.Repositories;

public sealed class ToolConfigurationAuditRepositoryTests
{
    // ── Constructor ──────────────────────────────────────────────────────

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenConnectionFactoryIsNull()
    {
        var act = () => new ToolConfigurationAuditRepository(null!, NullLogger<ToolConfigurationAuditRepository>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        var factory = new Mock<ISqlConnectionFactory>();

        var act = () => new ToolConfigurationAuditRepository(factory.Object, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ── RecordChangeAsync ────────────────────────────────────────────────

    [Fact]
    public async Task RecordChangeAsync_ShouldThrowArgumentException_WhenToolConfigurationIdIsEmpty()
    {
        var sut = CreateSut();

        var act = async () => await sut.RecordChangeAsync(Guid.Empty, "tool-1", "created", null, "{}", "user-1");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("toolConfigurationId");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task RecordChangeAsync_ShouldThrowArgumentException_WhenToolIdIsBlank(string? toolId)
    {
        var sut = CreateSut();

        var act = async () => await sut.RecordChangeAsync(Guid.NewGuid(), toolId!, "created", null, "{}", "user-1");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("toolId");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task RecordChangeAsync_ShouldThrowArgumentException_WhenActionIsBlank(string? action)
    {
        var sut = CreateSut();

        var act = async () => await sut.RecordChangeAsync(Guid.NewGuid(), "tool-1", action!, null, "{}", "user-1");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("action");
    }

    [Fact]
    public async Task RecordChangeAsync_ShouldCommitTransaction_AndReturnMappedEntry_WhenInsertSucceeds()
    {
        var toolConfigurationId = Guid.NewGuid();
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            RepositoryTestReader.CreateReader(new Dictionary<string, object?> { ["Id"] = 42L }),
            command =>
            {
                command.HasTransaction.Should().BeTrue();
                command.Parameters["ToolConfigurationId"].Should().Be(toolConfigurationId);
                command.Parameters["ToolId"].Should().Be("tool-1");
                command.Parameters["Action"].Should().Be("updated");
                command.Parameters["BeforeJson"].Should().Be("{\"before\":true}");
                command.Parameters["AfterJson"].Should().Be("{\"after\":true}");
                command.Parameters["UserId"].Should().Be("user-1");
                command.Parameters["ChangeReason"].Should().Be("config drift");
            });
        var sut = CreateSut(connection);

        var result = await sut.RecordChangeAsync(
            toolConfigurationId,
            "tool-1",
            "updated",
            "{\"before\":true}",
            "{\"after\":true}",
            "user-1",
            "config drift");

        result.Id.Should().Be(42L);
        result.ToolConfigurationId.Should().Be(toolConfigurationId);
        result.ToolId.Should().Be("tool-1");
        result.Action.Should().Be("updated");
        result.BeforeJson.Should().Be("{\"before\":true}");
        result.AfterJson.Should().Be("{\"after\":true}");
        result.UserId.Should().Be("user-1");
        result.ChangeReason.Should().Be("config drift");
        result.ChangedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));

        connection.BeginTransactionCount.Should().Be(1);
        connection.LastTransaction.Should().NotBeNull();
        connection.LastTransaction!.CommitCount.Should().Be(1);
        connection.LastTransaction.RollbackCount.Should().Be(0);
    }

    [Fact]
    public async Task RecordChangeAsync_ShouldRetainNullUserId_WhenNotProvided()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(RepositoryTestReader.CreateReader(new Dictionary<string, object?> { ["Id"] = 1L }));
        var sut = CreateSut(connection);

        var result = await sut.RecordChangeAsync(Guid.NewGuid(), "tool-1", "created", null, "{}", null);

        result.UserId.Should().BeNull();
    }

    [Fact]
    public async Task RecordChangeAsync_ShouldRollbackTransaction_AndWrapException_WhenInsertFails()
    {
        var innerException = new InvalidOperationException("insert failed");
        var connection = new FakeDbConnection();
        connection.EnqueueReaderException(innerException);
        var sut = CreateSut(connection);

        var act = async () => await sut.RecordChangeAsync(Guid.NewGuid(), "tool-1", "created", null, "{}", "user-1");

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Error recording audit entry for tool tool-1: created");
        exception.Which.InnerException.Should().Be(innerException);

        connection.BeginTransactionCount.Should().Be(1);
        connection.LastTransaction.Should().NotBeNull();
        connection.LastTransaction!.RollbackCount.Should().Be(1);
        connection.LastTransaction.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task RecordChangeAsync_ShouldWrapConnectionFailures_WithoutBeginningTransaction()
    {
        var expected = new InvalidOperationException("connection down");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.RecordChangeAsync(Guid.NewGuid(), "tool-1", "created", null, "{}", "user-1");

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Error recording audit entry for tool tool-1: created");
        exception.Which.InnerException.Should().Be(expected);
    }

    // ── GetAuditHistoryAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetAuditHistoryAsync_ShouldThrowArgumentException_WhenToolConfigurationIdIsEmpty()
    {
        var sut = CreateSut();

        var act = async () => await sut.GetAuditHistoryAsync(Guid.Empty);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("toolConfigurationId");
    }

    [Fact]
    public async Task GetAuditHistoryAsync_ShouldReturnMappedEntries_WhenRowsExist()
    {
        var toolConfigurationId = Guid.NewGuid();
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            RepositoryTestReader.CreateReader(CreateAuditRow(toolConfigurationId: toolConfigurationId)),
            command =>
            {
                command.Parameters["ToolConfigurationId"].Should().Be(toolConfigurationId);
                command.Parameters["Limit"].Should().Be(50);
                command.Parameters["Offset"].Should().Be(10);
            });
        var sut = CreateSut(connection);

        var result = await sut.GetAuditHistoryAsync(toolConfigurationId, limit: 50, offset: 10);

        result.Should().HaveCount(1);
        result[0].ToolConfigurationId.Should().Be(toolConfigurationId);
        result[0].ToolId.Should().Be("tool-1");
    }

    [Fact]
    public async Task GetAuditHistoryAsync_ShouldReturnEmptyArray_WhenNoRowsExist()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(RepositoryTestReader.CreateReaderWithSchema(
            "Id", "ToolConfigurationId", "ToolId", "Action", "BeforeJson", "AfterJson", "UserId", "ChangeReason", "ChangedAt", "IpAddress"));
        var sut = CreateSut(connection);

        var result = await sut.GetAuditHistoryAsync(Guid.NewGuid());

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAuditHistoryAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetAuditHistoryAsync(Guid.NewGuid());

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Error retrieving audit history for tool configuration");
        exception.Which.InnerException.Should().Be(expected);
    }

    // ── GetAuditEntriesByActionAsync ─────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task GetAuditEntriesByActionAsync_ShouldThrowArgumentException_WhenActionIsBlank(string? action)
    {
        var sut = CreateSut();

        var act = async () => await sut.GetAuditEntriesByActionAsync(action!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("action");
    }

    [Fact]
    public async Task GetAuditEntriesByActionAsync_ShouldReturnMappedEntries_WhenDateRangeProvided()
    {
        var fromDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var toDate = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            RepositoryTestReader.CreateReader(CreateAuditRow(action: "deleted")),
            command =>
            {
                command.Parameters["Action"].Should().Be("deleted");
                command.Parameters["Limit"].Should().Be(100);
                command.Parameters["FromDate"].Should().Be(fromDate);
                command.Parameters["ToDate"].Should().Be(toDate);
            });
        var sut = CreateSut(connection);

        var result = await sut.GetAuditEntriesByActionAsync("deleted", fromDate, toDate);

        result.Should().HaveCount(1);
        result[0].Action.Should().Be("deleted");
    }

    [Fact]
    public async Task GetAuditEntriesByActionAsync_ShouldThrow_WhenDatesAreOmitted_KnownDefect()
    {
        // Known production defect: DynamicParameters.Add("@FromDate", DBNull.Value)
        // causes Dapper to throw NotSupportedException from SqlMapper.LookupDbType
        // when the parameter value's runtime type is DBNull rather than a nullable
        // DateTime with an explicit DbType.
        var sut = CreateSut();

        var act = async () => await sut.GetAuditEntriesByActionAsync("created");

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.InnerException.Should().BeOfType<NotSupportedException>();
    }

    [Fact]
    public async Task GetAuditEntriesByActionAsync_ShouldReturnEmptyArray_WhenNoRowsExist()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(RepositoryTestReader.CreateReaderWithSchema(
            "Id", "ToolConfigurationId", "ToolId", "Action", "BeforeJson", "AfterJson", "UserId", "ChangeReason", "ChangedAt", "IpAddress"));
        var sut = CreateSut(connection);

        var result = await sut.GetAuditEntriesByActionAsync(
            "created",
            fromDate: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            toDate: new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAuditEntriesByActionAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetAuditEntriesByActionAsync("created");

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Error retrieving audit entries for action created");
        exception.Which.InnerException.Should().Be(expected);
    }

    // ── GetAuditEntriesByUserAsync ───────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task GetAuditEntriesByUserAsync_ShouldThrowArgumentException_WhenUserIdIsBlank(string? userId)
    {
        var sut = CreateSut();

        var act = async () => await sut.GetAuditEntriesByUserAsync(userId!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("userId");
    }

    [Fact]
    public async Task GetAuditEntriesByUserAsync_ShouldReturnMappedEntries_WhenRowsExist()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            RepositoryTestReader.CreateReader(CreateAuditRow(userId: "user-42")),
            command =>
            {
                command.Parameters["UserId"].Should().Be("user-42");
                command.Parameters["Limit"].Should().Be(25);
                command.Parameters["Offset"].Should().Be(5);
            });
        var sut = CreateSut(connection);

        var result = await sut.GetAuditEntriesByUserAsync("user-42", limit: 25, offset: 5);

        result.Should().HaveCount(1);
        result[0].UserId.Should().Be("user-42");
    }

    [Fact]
    public async Task GetAuditEntriesByUserAsync_ShouldReturnEmptyArray_WhenNoRowsExist()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(RepositoryTestReader.CreateReaderWithSchema(
            "Id", "ToolConfigurationId", "ToolId", "Action", "BeforeJson", "AfterJson", "UserId", "ChangeReason", "ChangedAt", "IpAddress"));
        var sut = CreateSut(connection);

        var result = await sut.GetAuditEntriesByUserAsync("user-42");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAuditEntriesByUserAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetAuditEntriesByUserAsync("user-42");

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Error retrieving audit entries for user user-42");
        exception.Which.InnerException.Should().Be(expected);
    }

    // ── GetAuditSummaryAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetAuditSummaryAsync_ShouldReturnMappedSummary_WhenRowExists()
    {
        var oldest = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var latest = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var connection = new FakeDbConnection();
        connection.EnqueueReader(RepositoryTestReader.CreateReader(new Dictionary<string, object?>
        {
            ["TotalEntries"] = 10,
            ["UniqueTools"] = 3,
            ["UniqueUsers"] = 2,
            ["OldestEntry"] = oldest,
            ["LatestEntry"] = latest
        }));
        var sut = CreateSut(connection);

        var result = await sut.GetAuditSummaryAsync();

        result.TotalEntries.Should().Be(10);
        result.UniqueTools.Should().Be(3);
        result.UniqueUsers.Should().Be(2);
        result.OldestEntry.Should().Be(oldest);
        result.LatestEntry.Should().Be(latest);
    }

    [Fact]
    public async Task GetAuditSummaryAsync_ShouldReturnDefaultSummary_WhenNoRowExists()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(RepositoryTestReader.CreateReaderWithSchema(
            "TotalEntries", "UniqueTools", "UniqueUsers", "OldestEntry", "LatestEntry"));
        var sut = CreateSut(connection);

        var result = await sut.GetAuditSummaryAsync();

        result.Should().NotBeNull();
        result.TotalEntries.Should().Be(0);
        result.UniqueTools.Should().Be(0);
        result.UniqueUsers.Should().Be(0);
        result.OldestEntry.Should().BeNull();
        result.LatestEntry.Should().BeNull();
    }

    [Fact]
    public async Task GetAuditSummaryAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetAuditSummaryAsync();

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Error retrieving audit summary");
        exception.Which.InnerException.Should().Be(expected);
    }

    // ── PurgeOldEntriesAsync ─────────────────────────────────────────────

    [Fact]
    public async Task PurgeOldEntriesAsync_ShouldReturnDeletedCount_UsingDefaultRetention()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(7, command =>
            command.Parameters["RetentionDays"].Should().Be(90));
        var sut = CreateSut(connection);

        var result = await sut.PurgeOldEntriesAsync();

        result.Should().Be(7);
    }

    [Fact]
    public async Task PurgeOldEntriesAsync_ShouldReturnDeletedCount_UsingCustomRetention()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(2, command =>
            command.Parameters["RetentionDays"].Should().Be(30));
        var sut = CreateSut(connection);

        var result = await sut.PurgeOldEntriesAsync(retentionDays: 30);

        result.Should().Be(2);
    }

    [Fact]
    public async Task PurgeOldEntriesAsync_ShouldReturnZero_WhenNoEntriesDeleted()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(0);
        var sut = CreateSut(connection);

        var result = await sut.PurgeOldEntriesAsync();

        result.Should().Be(0);
    }

    [Fact]
    public async Task PurgeOldEntriesAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.PurgeOldEntriesAsync();

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Error purging old audit entries");
        exception.Which.InnerException.Should().Be(expected);
    }

    // ── Test helpers ─────────────────────────────────────────────────────

    private static Dictionary<string, object?> CreateAuditRow(
        long id = 1L,
        Guid? toolConfigurationId = null,
        string toolId = "tool-1",
        string action = "created",
        string? userId = "user-1") =>
        new()
        {
            ["Id"] = id,
            ["ToolConfigurationId"] = toolConfigurationId ?? Guid.NewGuid(),
            ["ToolId"] = toolId,
            ["Action"] = action,
            ["BeforeJson"] = (string?)null,
            ["AfterJson"] = "{}",
            ["UserId"] = userId,
            ["ChangeReason"] = (string?)null,
            ["ChangedAt"] = new DateTime(2026, 7, 12, 9, 0, 0, DateTimeKind.Utc),
            ["IpAddress"] = (string?)null
        };

    private static ToolConfigurationAuditRepository CreateSut(FakeDbConnection? connection = null)
    {
        var factory = new Mock<ISqlConnectionFactory>();
        factory
            .Setup(x => x.CreateOpenConnectionAsync())
            .ReturnsAsync(connection ?? new FakeDbConnection());
        return new ToolConfigurationAuditRepository(factory.Object, NullLogger<ToolConfigurationAuditRepository>.Instance);
    }

    private static ToolConfigurationAuditRepository CreateThrowingSut(Exception exception)
    {
        var factory = new Mock<ISqlConnectionFactory>();
        factory.Setup(f => f.CreateOpenConnectionAsync()).ThrowsAsync(exception);
        return new ToolConfigurationAuditRepository(factory.Object, NullLogger<ToolConfigurationAuditRepository>.Instance);
    }
}
