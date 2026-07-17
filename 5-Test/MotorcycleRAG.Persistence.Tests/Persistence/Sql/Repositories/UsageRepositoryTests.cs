using System.Collections;
using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Persistence.Sql;
using MotorcycleRAG.Persistence.Sql.Repositories;

namespace MotorcycleRAG.UnitTests.Persistence.Sql.Repositories;

public sealed class UsageRepositoryTests
{
    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenConnectionFactoryIsNull()
    {
        var act = () => new UsageRepository(null!, NullLogger<UsageRepository>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        var factory = new Mock<ISqlConnectionFactory>();

        var act = () => new UsageRepository(factory.Object, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task RecordUsageAsync_ShouldThrowArgumentNullException_WhenUsageIsNull()
    {
        var sut = CreateSut();

        var act = async () => await sut.RecordUsageAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("usage");
    }

    [Fact]
    public async Task RecordUsageAsync_ShouldReturnUsageWithAssignedId()
    {
        var usage = CreateUsage();
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            CreateReader(new Dictionary<string, object?> { ["Id"] = 99L }),
            command =>
            {
                command.CommandText.Should().Contain("INSERT INTO [dbo].[Usage]");
                command.Parameters["UserId"].Should().Be(usage.UserId);
                command.Parameters["Endpoint"].Should().Be(usage.Endpoint);
                command.Parameters["HttpMethod"].Should().Be(usage.HttpMethod);
            });
        var sut = CreateSut(connection);

        var result = await sut.RecordUsageAsync(usage);

        result.Should().BeSameAs(usage);
        result.Id.Should().Be(99);
        connection.ExecutedCommands.Should().ContainSingle();
    }

    [Fact]
    public async Task RecordUsageAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.RecordUsageAsync(CreateUsage());

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Failed to record usage for user user-1");
        exception.Which.InnerException.Should().Be(expected);
    }

    [Fact]
    public async Task RecordUsageAsync_WhenInsertFails_RollsBackTransactionAndWrapsFailure()
    {
        var expected = new DataException("insert failed");
        var connection = new FakeDbConnection();
        connection.EnqueueReaderException(expected);
        var sut = CreateSut(connection);

        var act = () => sut.RecordUsageAsync(CreateUsage());

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.InnerException.Should().Be(expected);
        connection.LastTransaction.Should().NotBeNull();
        connection.LastTransaction!.WasRolledBack.Should().BeTrue();
        connection.LastTransaction.WasCommitted.Should().BeFalse();
    }

    [Fact]
    public async Task RecordSeedUsageAsync_ShouldThrowArgumentNullException_WhenUsageIsNull()
    {
        var sut = CreateSut();

        var act = async () => await sut.RecordSeedUsageAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("usage");
    }

    [Fact]
    public async Task RecordSeedUsageAsync_ShouldReturnMappedRow()
    {
        var usage = CreateUsage();
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            CreateReader(new Dictionary<string, object?>
            {
                ["Id"] = 42L,
                ["UserId"] = usage.UserId,
                ["Endpoint"] = usage.Endpoint,
                ["HttpMethod"] = usage.HttpMethod,
                ["QueryId"] = usage.QueryId,
                ["RequestTime"] = usage.RequestTime,
                ["DurationMs"] = usage.DurationMs,
                ["StatusCode"] = usage.StatusCode,
                ["IsSuccess"] = usage.IsSuccess,
                ["CallerIp"] = usage.CallerIp,
                ["UserAgent"] = usage.UserAgent
            }),
            command =>
            {
                command.CommandText.Should().Contain("IF EXISTS");
                command.Parameters["UserId"].Should().Be(usage.UserId);
                command.Parameters["QueryId"].Should().Be(usage.QueryId);
            });
        var sut = CreateSut(connection);

        var result = await sut.RecordSeedUsageAsync(usage);

        result.Should().NotBeNull();
        result!.Id.Should().Be(42);
        result.UserId.Should().Be(usage.UserId);
        result.QueryId.Should().Be(usage.QueryId);
    }

    [Fact]
    public async Task RecordSeedUsageAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.RecordSeedUsageAsync(CreateUsage());

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Failed to record onboarding seed usage for user user-1");
        exception.Which.InnerException.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task GetUsageByUserAndDateRangeAsync_ShouldThrowArgumentException_WhenUserIdIsBlank(string? userId)
    {
        var sut = CreateSut();

        var act = async () => await sut.GetUsageByUserAndDateRangeAsync(userId!, DateTime.UtcNow, DateTime.UtcNow);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("userId");
    }

    [Fact]
    public async Task GetUsageByUserAndDateRangeAsync_ShouldReturnRecords()
    {
        var userId = "user-1";
        var startDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            CreateReader(
                CreateUsageRow(1, userId),
                CreateUsageRow(2, userId)),
            command =>
            {
                command.CommandText.Should().Contain("BETWEEN @StartDate AND @EndDate");
                command.Parameters["UserId"].Should().Be(userId);
                command.Parameters["StartDate"].Should().Be(startDate);
                command.Parameters["EndDate"].Should().Be(endDate);
            });
        var sut = CreateSut(connection);

        var results = await sut.GetUsageByUserAndDateRangeAsync(userId, startDate, endDate);

        results.Should().HaveCount(2);
        results[0].UserId.Should().Be(userId);
        results[1].UserId.Should().Be(userId);
    }

    [Fact]
    public async Task GetUsageByUserAndDateRangeAsync_ShouldReturnEmpty_WhenNoRecords()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(CreateReader());
        var sut = CreateSut(connection);

        var results = await sut.GetUsageByUserAndDateRangeAsync("user-1", DateTime.UtcNow, DateTime.UtcNow);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUsageByUserAndDateRangeAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetUsageByUserAndDateRangeAsync("user-1", DateTime.UtcNow, DateTime.UtcNow);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Failed to get usage for user user-1");
        exception.Which.InnerException.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task GetDailyUsageCountAsync_ShouldThrowArgumentException_WhenUserIdIsBlank(string? userId)
    {
        var sut = CreateSut();

        var act = async () => await sut.GetDailyUsageCountAsync(userId!, DateTime.UtcNow);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("userId");
    }

    [Fact]
    public async Task GetDailyUsageCountAsync_ShouldReturnCount()
    {
        var date = new DateTime(2026, 7, 10);
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            CreateReader(new Dictionary<string, object?> { ["Count"] = 5 }),
            command =>
            {
                command.CommandText.Should().Contain("sp_GetDailyUsageCount");
                command.Parameters["UserId"].Should().Be("user-1");
            });
        var sut = CreateSut(connection);

        var result = await sut.GetDailyUsageCountAsync("user-1", date);

        result.Should().Be(5);
    }

    [Fact]
    public async Task GetDailyUsageCountAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetDailyUsageCountAsync("user-1", DateTime.UtcNow);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Failed to get daily usage count for user user-1");
        exception.Which.InnerException.Should().Be(expected);
    }

    private static UsageRepository CreateSut(FakeDbConnection? connection = null)
    {
        var factory = new Mock<ISqlConnectionFactory>();
        factory
            .Setup(x => x.CreateOpenConnectionAsync())
            .ReturnsAsync(connection ?? new FakeDbConnection());
        return new UsageRepository(factory.Object, NullLogger<UsageRepository>.Instance);
    }

    private static UsageRepository CreateThrowingSut(Exception exception)
    {
        var factory = new Mock<ISqlConnectionFactory>();
        factory.Setup(x => x.CreateOpenConnectionAsync()).ThrowsAsync(exception);
        return new UsageRepository(factory.Object, NullLogger<UsageRepository>.Instance);
    }

    private static Usage CreateUsage() => new()
    {
        UserId = "user-1",
        Endpoint = "/api/query",
        HttpMethod = "POST",
        QueryId = "q-1",
        RequestTime = new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc),
        DurationMs = 150,
        StatusCode = 200,
        IsSuccess = true,
        CallerIp = "127.0.0.1",
        UserAgent = "test-agent"
    };

    private static Dictionary<string, object?> CreateUsageRow(long id, string userId = "user-1") => new()
    {
        ["Id"] = id,
        ["UserId"] = userId,
        ["Endpoint"] = "/api/query",
        ["HttpMethod"] = "POST",
        ["QueryId"] = "q-1",
        ["RequestTime"] = new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc),
        ["DurationMs"] = 150L,
        ["StatusCode"] = 200,
        ["IsSuccess"] = true,
        ["CallerIp"] = "127.0.0.1",
        ["UserAgent"] = "test-agent"
    };

    private static DbDataReader CreateReader(params IReadOnlyDictionary<string, object?>[] rows)
    {
        var table = new DataTable();
        if (rows.Length == 0)
        {
            table.Columns.Add("Id", typeof(object));
            return table.CreateDataReader();
        }

        var columnNames = rows.SelectMany(static row => row.Keys).Distinct(StringComparer.Ordinal).ToList();
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
        public FakeDbTransaction? LastTransaction { get; private set; }

        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "Fake";
        public override string DataSource => "Fake";
        public override string ServerVersion => "1.0";
        public override ConnectionState State => ConnectionState.Open;

        public void EnqueueScalar(object? result, Action<ExecutedCommand>? assert = null)
        {
            _plans.Enqueue(new CommandPlan(CommandKind.Scalar, () => result, assert));
        }

        public void EnqueueNonQuery(int affectedRows, Action<ExecutedCommand>? assert = null)
        {
            _plans.Enqueue(new CommandPlan(CommandKind.NonQuery, () => affectedRows, assert));
        }

        public void EnqueueReader(DbDataReader reader, Action<ExecutedCommand>? assert = null)
        {
            _plans.Enqueue(new CommandPlan(CommandKind.Reader, () => reader, assert));
        }

        public void EnqueueReaderException(Exception exception) =>
            _plans.Enqueue(new CommandPlan(CommandKind.Reader, () => throw exception, null));

        public override void ChangeDatabase(string databaseName) { }
        public override void Close() { }
        public override void Open() { }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
            => LastTransaction = new FakeDbTransaction(this, isolationLevel);

        protected override DbCommand CreateDbCommand() => new FakeDbCommand(this);

        internal object? Execute(CommandKind kind, FakeDbCommand command)
        {
            _plans.Should().NotBeEmpty("every repository call in these tests should have a planned DB response");
            var plan = _plans.Dequeue();
            plan.Kind.Should().Be(kind);

            var executed = new ExecutedCommand(command.CommandText, command.GetParameters());
            ExecutedCommands.Add(executed);
            plan.Assert?.Invoke(executed);
            return plan.ResultFactory();
        }
    }

    private sealed class FakeDbTransaction : DbTransaction
    {
        private readonly DbConnection _connection;
        public FakeDbTransaction(DbConnection connection, IsolationLevel isolationLevel)
        {
            _connection = connection;
            IsolationLevel = isolationLevel;
        }
        public override IsolationLevel IsolationLevel { get; }
        protected override DbConnection? DbConnection => _connection;
        public bool WasCommitted { get; private set; }
        public bool WasRolledBack { get; private set; }
        public override void Commit() => WasCommitted = true;
        public override void Rollback() => WasRolledBack = true;
    }

    private sealed class FakeDbCommand : DbCommand
    {
        private readonly FakeDbConnection _connection;
        private readonly FakeDbParameterCollection _parameters = new();

        public FakeDbCommand(FakeDbConnection connection)
        {
            _connection = connection;
        }

        public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection DbConnection
        {
            get => _connection;
            set => throw new NotSupportedException();
        }

        protected override DbParameterCollection DbParameterCollection => _parameters;
        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel() { }
        public override int ExecuteNonQuery() => (int)(_connection.Execute(CommandKind.NonQuery, this) ?? 0);
        public override object? ExecuteScalar() => _connection.Execute(CommandKind.Scalar, this);
        public override void Prepare() { }
        protected override DbParameter CreateDbParameter() => new FakeDbParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
            (DbDataReader)(_connection.Execute(CommandKind.Reader, this)
                ?? throw new InvalidOperationException("Reader result was null."));

        internal Dictionary<string, object?> GetParameters() =>
            _parameters
                .Cast<FakeDbParameter>()
                .ToDictionary(parameter => parameter.ParameterName, parameter => parameter.Value, StringComparer.Ordinal);
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
        public override string ParameterName { get; set; } = string.Empty;
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
        IReadOnlyDictionary<string, object?> Parameters);

    private enum CommandKind
    {
        Scalar,
        NonQuery,
        Reader
    }
}
