using System.Collections;
using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Persistence.Sql;
using MotorcycleRAG.Persistence.Sql.Repositories;

namespace MotorcycleRAG.UnitTests.Persistence.Sql.Repositories;

public sealed class UserManagementQueryRepositoryTests
{
    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenConnectionFactoryIsNull()
    {
        var act = () => new UserManagementQueryRepository(null!, NullLogger<UserManagementQueryRepository>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        var factory = new Mock<ISqlConnectionFactory>();

        var act = () => new UserManagementQueryRepository(factory.Object, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task GetRowsAsync_ShouldThrowArgumentException_WhenPageLessThanOne()
    {
        var sut = CreateSut();

        var act = async () => await sut.GetRowsAsync(null, null, 0, 10);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("page");
    }

    [Fact]
    public async Task GetRowsAsync_ShouldThrowArgumentException_WhenPageSizeLessThanOne()
    {
        var sut = CreateSut();

        var act = async () => await sut.GetRowsAsync(null, null, 1, 0);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("pageSize");
    }

    [Fact]
    public async Task GetRowsAsync_ShouldReturnPagedResults()
    {
        var connection = new FakeDbConnection();
        var row1 = CreateManagementRow("request:abc-123", "PendingRequest");
        var row2 = CreateManagementRow("user:def-456", "ManagedUser");
        var firstTable = CreateTable(
            ["RowId", "RowType", "AccessRequestId", "ManagedUserId", "Email", "Provider",
             "AssignedTier", "RequestDecisionState", "OnboardingExecutionState", "ManagedUserAccessState",
             "RowState", "AllowedActions", "CorrelationId", "RequestedAtUtc", "ApprovedAtUtc",
             "CancelledAtUtc", "LastFailureCode", "LastFailureMessage", "RowVersion"],
            row1, row2);
        var countTable = CreateTable(["Count"], new Dictionary<string, object?> { ["Count"] = 42 });

        var dataSet = new DataSet();
        dataSet.Tables.Add(firstTable);
        dataSet.Tables.Add(countTable);

        connection.EnqueueReader(
            dataSet.CreateDataReader(),
            command =>
            {
                command.CommandText.Should().Contain("OFFSET @Offset");
                command.Parameters["Offset"].Should().Be(0);
                command.Parameters["PageSize"].Should().Be(10);
            });
        var sut = CreateSut(connection);

        var result = await sut.GetRowsAsync(null, null, 1, 10);

        result.Should().NotBeNull();
        result.Rows.Should().HaveCount(2);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(10);
        result.TotalCount.Should().Be(42);
        result.Rows[0].RowId.Should().Be("request:abc-123");
        result.Rows[0].RowType.Should().Be("PendingRequest");
        result.Rows[1].RowId.Should().Be("user:def-456");
        result.Rows[1].RowType.Should().Be("ManagedUser");
    }

    [Fact]
    public async Task GetRowsAsync_ShouldFilterByRowState_WhenProvided()
    {
        var connection = new FakeDbConnection();
        var firstTable = CreateTable(
            ["RowId", "RowType", "AccessRequestId", "ManagedUserId", "Email", "Provider",
             "AssignedTier", "RequestDecisionState", "OnboardingExecutionState", "ManagedUserAccessState",
             "RowState", "AllowedActions", "CorrelationId", "RequestedAtUtc", "ApprovedAtUtc",
             "CancelledAtUtc", "LastFailureCode", "LastFailureMessage", "RowVersion"]);
        var countTable = CreateTable(["Count"], new Dictionary<string, object?> { ["Count"] = 0 });

        var dataSet = new DataSet();
        dataSet.Tables.Add(firstTable);
        dataSet.Tables.Add(countTable);

        connection.EnqueueReader(
            dataSet.CreateDataReader(),
            command =>
            {
                command.Parameters["RowState"].Should().Be("Active");
            });
        var sut = CreateSut(connection);

        var result = await sut.GetRowsAsync(UserManagementRowState.Active, null, 1, 10);

        result.Rows.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetRowsAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetRowsAsync(null, null, 1, 10);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Failed to query user-management rows");
        exception.Which.InnerException.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task GetRowByIdAsync_ShouldThrowArgumentException_WhenRowIdIsBlank(string? rowId)
    {
        var sut = CreateSut();

        var act = async () => await sut.GetRowByIdAsync(rowId!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("rowId");
    }

    [Fact]
    public async Task GetRowByIdAsync_ShouldReturnMappedRow_WhenFound()
    {
        var rowId = "user:managed-1";
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            CreateTable(
                ["RowId", "RowType", "AccessRequestId", "ManagedUserId", "Email", "Provider",
                 "AssignedTier", "RequestDecisionState", "OnboardingExecutionState", "ManagedUserAccessState",
                 "RowState", "AllowedActions", "CorrelationId", "RequestedAtUtc", "ApprovedAtUtc",
                 "CancelledAtUtc", "LastFailureCode", "LastFailureMessage", "RowVersion"],
                CreateManagementRow(rowId, "ManagedUser", managedUserId: "managed-1")).CreateDataReader(),
            command =>
            {
                command.Parameters["ExactRowId"].Should().Be(rowId);
                command.Parameters["RequestId"].Should().Be(DBNull.Value);
                command.Parameters["ManagedUserId"].Should().Be("managed-1");
            });
        var sut = CreateSut(connection);

        var result = await sut.GetRowByIdAsync(rowId);

        result.Should().NotBeNull();
        result!.RowId.Should().Be(rowId);
        result.RowType.Should().Be("ManagedUser");
        result.ManagedUserId.Should().Be("managed-1");
        result.Email.Should().Be("rider@example.com");
        result.Provider.Should().Be(IdentityProvider.Microsoft);
    }

    [Fact]
    public async Task GetRowByIdAsync_ShouldReturnNull_WhenNotFound()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(CreateTable(["RowId"]).CreateDataReader());
        var sut = CreateSut(connection);

        var result = await sut.GetRowByIdAsync("user:nonexistent");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetRowByIdAsync_ShouldHandleRequestPrefix()
    {
        var rowId = "request:abc-123";
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            CreateTable(
                ["RowId", "RowType", "AccessRequestId", "ManagedUserId", "Email", "Provider",
                 "AssignedTier", "RequestDecisionState", "OnboardingExecutionState", "ManagedUserAccessState",
                 "RowState", "AllowedActions", "CorrelationId", "RequestedAtUtc", "ApprovedAtUtc",
                 "CancelledAtUtc", "LastFailureCode", "LastFailureMessage", "RowVersion"],
                CreateManagementRow(rowId, "PendingRequest", accessRequestId: "abc-123")).CreateDataReader(),
            command =>
            {
                command.Parameters["RequestId"].Should().Be("abc-123");
                command.Parameters["ManagedUserId"].Should().Be(DBNull.Value);
            });
        var sut = CreateSut(connection);

        var result = await sut.GetRowByIdAsync(rowId);

        result.Should().NotBeNull();
        result!.RowId.Should().Be(rowId);
        result.AccessRequestId.Should().Be("abc-123");
    }

    [Fact]
    public async Task GetRowByIdAsync_ShouldFallback_WhenNoPrefix()
    {
        var rowId = "plain-id";
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            CreateTable(
                ["RowId", "RowType", "AccessRequestId", "ManagedUserId", "Email", "Provider",
                 "AssignedTier", "RequestDecisionState", "OnboardingExecutionState", "ManagedUserAccessState",
                 "RowState", "AllowedActions", "CorrelationId", "RequestedAtUtc", "ApprovedAtUtc",
                 "CancelledAtUtc", "LastFailureCode", "LastFailureMessage", "RowVersion"],
                CreateManagementRow(rowId, "ManagedUser", managedUserId: "plain-id")).CreateDataReader(),
            command =>
            {
                command.Parameters["RequestId"].Should().Be("plain-id");
                command.Parameters["ManagedUserId"].Should().Be("plain-id");
            });
        var sut = CreateSut(connection);

        var result = await sut.GetRowByIdAsync(rowId);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetRowByIdAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetRowByIdAsync("user:managed-1");

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Failed to query management row user:managed-1");
        exception.Which.InnerException.Should().Be(expected);
    }

    private static UserManagementQueryRepository CreateSut(FakeDbConnection? connection = null)
    {
        var factory = new Mock<ISqlConnectionFactory>();
        factory
            .Setup(x => x.CreateOpenConnectionAsync())
            .ReturnsAsync(connection ?? new FakeDbConnection());
        return new UserManagementQueryRepository(factory.Object, NullLogger<UserManagementQueryRepository>.Instance);
    }

    private static UserManagementQueryRepository CreateThrowingSut(Exception exception)
    {
        var factory = new Mock<ISqlConnectionFactory>();
        factory.Setup(x => x.CreateOpenConnectionAsync()).ThrowsAsync(exception);
        return new UserManagementQueryRepository(factory.Object, NullLogger<UserManagementQueryRepository>.Instance);
    }

    private static Dictionary<string, object?> CreateManagementRow(
        string rowId,
        string rowType,
        string? accessRequestId = null,
        string? managedUserId = null,
        string email = "rider@example.com",
        string provider = "Microsoft",
        string? assignedTier = "Trial",
        string requestDecision = "Approved",
        string onboardingExec = "Completed",
        string managedAccess = "Active",
        string rowStateStr = "Active",
        string allowedActions = "ChangeTier,Cancel") => new()
    {
        ["RowId"] = rowId,
        ["RowType"] = rowType,
        ["AccessRequestId"] = accessRequestId,
        ["ManagedUserId"] = managedUserId,
        ["Email"] = email,
        ["Provider"] = provider,
        ["AssignedTier"] = assignedTier,
        ["RequestDecisionState"] = requestDecision,
        ["OnboardingExecutionState"] = onboardingExec,
        ["ManagedUserAccessState"] = managedAccess,
        ["RowState"] = rowStateStr,
        ["AllowedActions"] = allowedActions,
        ["CorrelationId"] = "corr-1",
        ["RequestedAtUtc"] = new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc),
        ["ApprovedAtUtc"] = new DateTime(2026, 7, 10, 13, 0, 0, DateTimeKind.Utc),
        ["CancelledAtUtc"] = DBNull.Value,
        ["LastFailureCode"] = DBNull.Value,
        ["LastFailureMessage"] = DBNull.Value,
        ["RowVersion"] = "0x01"
    };

    private static DataTable CreateTable(string[] columnNames, params IReadOnlyDictionary<string, object?>[] rows)
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

        return table;
    }

    private static DbDataReader CreateReader(params IReadOnlyDictionary<string, object?>[] rows)
    {
        var table = new DataTable();
        if (rows.Length == 0)
        {
            table.Columns.Add("RowId", typeof(object));
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

        public override void ChangeDatabase(string databaseName) { }
        public override void Close() { }
        public override void Open() { }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
            => throw new NotSupportedException();

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
