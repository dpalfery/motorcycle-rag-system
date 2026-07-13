using System.Collections;
using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;
using MotorcycleRAG.Persistence.Sql;
using MotorcycleRAG.Persistence.Sql.Repositories;

namespace MotorcycleRAG.UnitTests.Persistence.Sql.Repositories;

public sealed class WebScrapeRunRepositoryTests
{
    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenConnectionFactoryIsNull()
    {
        var act = () => new WebScrapeRunRepository(null!, NullLogger<WebScrapeRunRepository>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        var factory = new Mock<ISqlConnectionFactory>();

        var act = () => new WebScrapeRunRepository(factory.Object, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task CreateWebScrapeRunAsync_ShouldReturnRunId()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            CreateReader(new Dictionary<string, object?> { ["Id"] = 123L }),
            command =>
            {
                command.CommandText.Should().Contain("INSERT INTO [dbo].[WebSourceCrawlResults]");
                command.Parameters["WebSourceId"].Should().Be(5);
                command.Parameters["Status"].Should().Be((int)ScrapeRunStatus.Pending);
            });
        var sut = CreateSut(connection);

        var result = await sut.CreateWebScrapeRunAsync(5);

        result.Should().Be(123);
    }

    [Fact]
    public async Task CreateWebScrapeRunAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.CreateWebScrapeRunAsync(5);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Failed to create web scrape run for web source 5");
        exception.Which.InnerException.Should().Be(expected);
    }

    [Fact]
    public async Task UpdateWebScrapeRunAsync_ShouldReturnTrue_WhenRowsAffected()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(
            1,
            command =>
            {
                command.CommandText.Should().Contain("UPDATE [dbo].[WebSourceCrawlResults]");
                command.Parameters["RunId"].Should().Be(42L);
                command.Parameters["Status"].Should().Be((int)ScrapeRunStatus.Completed);
                command.Parameters["PagesCrawled"].Should().Be(10);
                command.Parameters["PagesIndexed"].Should().Be(8);
                command.Parameters["Errors"].Should().Be(2);
                command.Parameters["ErrorMessage"].Should().Be("timeout on 2 pages");
            });
        var sut = CreateSut(connection);

        var result = await sut.UpdateWebScrapeRunAsync(42, ScrapeRunStatus.Completed, 10, 8, 2, "timeout on 2 pages");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateWebScrapeRunAsync_ShouldReturnFalse_WhenNoRowsAffected()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(0);
        var sut = CreateSut(connection);

        var result = await sut.UpdateWebScrapeRunAsync(99, ScrapeRunStatus.Completed, 0, 0, 0, null);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateWebScrapeRunAsync_ShouldAllowNullErrorMessage()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(
            1,
            command =>
            {
                command.Parameters["ErrorMessage"].Should().Be(DBNull.Value);
            });
        var sut = CreateSut(connection);

        var result = await sut.UpdateWebScrapeRunAsync(42, ScrapeRunStatus.Completed, 5, 5, 0, null);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateWebScrapeRunAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.UpdateWebScrapeRunAsync(42, ScrapeRunStatus.Completed, 0, 0, 0, null);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Failed to update web scrape run 42");
        exception.Which.InnerException.Should().Be(expected);
    }

    [Fact]
    public async Task GetWebScrapeRunAsync_ShouldReturnRun_WhenFound()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            CreateReader(CreateRunRow(42, 5, "Completed")),
            command =>
            {
                command.Parameters["RunId"].Should().Be(42L);
            });
        var sut = CreateSut(connection);

        var result = await sut.GetWebScrapeRunAsync(42);

        result.Should().NotBeNull();
        result!.Id.Should().Be(42);
        result.WebSourceId.Should().Be(5);
        result.Status.Should().Be("Completed");
        result.PagesCrawled.Should().Be(10);
        result.PagesIndexed.Should().Be(8);
    }

    [Fact]
    public async Task GetWebScrapeRunAsync_ShouldReturnNull_WhenNotFound()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(CreateReader());
        var sut = CreateSut(connection);

        var result = await sut.GetWebScrapeRunAsync(999);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetWebScrapeRunAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetWebScrapeRunAsync(42);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Failed to get web scrape run 42");
        exception.Which.InnerException.Should().Be(expected);
    }

    [Fact]
    public async Task GetRecentScrapeRunsAsync_ShouldReturnRuns()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            CreateReader(
                CreateRunRow(3, 5, "Completed"),
                CreateRunRow(2, 5, "Failed"),
                CreateRunRow(1, 5, "Completed")),
            command =>
            {
                command.CommandText.Should().Contain("TOP (@Limit)");
                command.Parameters["WebSourceId"].Should().Be(5);
                command.Parameters["Limit"].Should().Be(5);
            });
        var sut = CreateSut(connection);

        var results = await sut.GetRecentScrapeRunsAsync(5, 5);

        results.Should().HaveCount(3);
        results[0].Id.Should().Be(3);
    }

    [Fact]
    public async Task GetRecentScrapeRunsAsync_ShouldUseDefaultLimit()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(CreateReader());
        var sut = CreateSut(connection);

        var results = await sut.GetRecentScrapeRunsAsync(5);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRecentScrapeRunsAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetRecentScrapeRunsAsync(5);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Failed to get recent scrape runs for web source 5");
        exception.Which.InnerException.Should().Be(expected);
    }

    [Fact]
    public async Task GetActiveScrapeRunsAsync_ShouldReturnActiveRuns()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            CreateReader(
                CreateRunRow(1, 5, "Pending"),
                CreateRunRow(2, 10, "Running")),
            command =>
            {
                command.Parameters["PendingStatus"].Should().Be((int)ScrapeRunStatus.Pending);
                command.Parameters["RunningStatus"].Should().Be((int)ScrapeRunStatus.Running);
            });
        var sut = CreateSut(connection);

        var results = await sut.GetActiveScrapeRunsAsync();

        results.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetActiveScrapeRunsAsync_ShouldReturnEmpty_WhenNoActive()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(CreateReader());
        var sut = CreateSut(connection);

        var results = await sut.GetActiveScrapeRunsAsync();

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetActiveScrapeRunsAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetActiveScrapeRunsAsync();

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Failed to get active scrape runs");
        exception.Which.InnerException.Should().Be(expected);
    }

    private static WebScrapeRunRepository CreateSut(FakeDbConnection? connection = null)
    {
        var factory = new Mock<ISqlConnectionFactory>();
        factory
            .Setup(x => x.CreateOpenConnectionAsync())
            .ReturnsAsync(connection ?? new FakeDbConnection());
        return new WebScrapeRunRepository(factory.Object, NullLogger<WebScrapeRunRepository>.Instance);
    }

    private static WebScrapeRunRepository CreateThrowingSut(Exception exception)
    {
        var factory = new Mock<ISqlConnectionFactory>();
        factory.Setup(x => x.CreateOpenConnectionAsync()).ThrowsAsync(exception);
        return new WebScrapeRunRepository(factory.Object, NullLogger<WebScrapeRunRepository>.Instance);
    }

    private static Dictionary<string, object?> CreateRunRow(long id, int webSourceId, string status) => new()
    {
        ["Id"] = id,
        ["WebSourceId"] = webSourceId,
        ["CrawlStartTime"] = new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc),
        ["CrawlEndTime"] = status == "Completed"
            ? new DateTime(2026, 7, 10, 13, 0, 0, DateTimeKind.Utc)
            : DBNull.Value,
        ["Status"] = status,
        ["PagesCrawled"] = 10,
        ["PagesIndexed"] = 8,
        ["Errors"] = 0,
        ["ErrorMessage"] = DBNull.Value
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
