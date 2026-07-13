using System.Collections;
using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Persistence.Sql;
using MotorcycleRAG.Persistence.Sql.Repositories;

namespace MotorcycleRAG.UnitTests.Persistence.Sql.Repositories;

public sealed class WebSourceRepositoryTests
{
    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenConnectionFactoryIsNull()
    {
        var act = () => new WebSourceRepository(null!, NullLogger<WebSourceRepository>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        var factory = new Mock<ISqlConnectionFactory>();

        var act = () => new WebSourceRepository(factory.Object, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task CreateWebSourceAsync_ShouldThrowArgumentNullException_WhenWebSourceIsNull()
    {
        var sut = CreateSut();

        var act = async () => await sut.CreateWebSourceAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("webSource");
    }

    [Fact]
    public async Task CreateWebSourceAsync_ShouldReturnWebSourceWithAssignedId()
    {
        var webSource = CreateWebSource();
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            CreateReader(new Dictionary<string, object?> { ["Id"] = 42 }),
            command =>
            {
                command.CommandText.Should().Contain("INSERT INTO [dbo].[WebSources]");
                command.Parameters["Url"].Should().Be(webSource.Url);
                command.Parameters["Name"].Should().Be(webSource.Name);
                command.Parameters["IsEnabled"].Should().Be(true);
                command.Parameters["TrustTier"].Should().Be(3);
                command.Parameters["IncludeInSearch"].Should().Be(true);
                command.Parameters["MaxCrawlDepth"].Should().Be(2);
            });
        var sut = CreateSut(connection);

        var result = await sut.CreateWebSourceAsync(webSource);

        result.Should().BeSameAs(webSource);
        result.Id.Should().Be(42);
    }

    [Fact]
    public async Task CreateWebSourceAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.CreateWebSourceAsync(CreateWebSource());

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Failed to create web source");
        exception.Which.InnerException.Should().Be(expected);
    }

    [Fact]
    public async Task GetWebSourceByIdAsync_ShouldReturnWebSource_WhenFound()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            CreateReader(CreateWebSourceRow(5, "https://example.com", "Example Site")),
            command =>
            {
                command.Parameters["WebSourceId"].Should().Be(5);
            });
        var sut = CreateSut(connection);

        var result = await sut.GetWebSourceByIdAsync(5);

        result.Should().NotBeNull();
        result!.Id.Should().Be(5);
        result.Url.Should().Be("https://example.com");
        result.Name.Should().Be("Example Site");
        result.IsEnabled.Should().BeTrue();
        result.TrustTier.Should().Be(3);
    }

    [Fact]
    public async Task GetWebSourceByIdAsync_ShouldReturnNull_WhenNotFound()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(CreateReader());
        var sut = CreateSut(connection);

        var result = await sut.GetWebSourceByIdAsync(999);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetWebSourceByIdAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetWebSourceByIdAsync(5);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Failed to get web source by ID 5");
        exception.Which.InnerException.Should().Be(expected);
    }

    [Fact]
    public async Task GetAllWebSourcesAsync_ShouldReturnAllSources()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            CreateReader(
                CreateWebSourceRow(1, "https://site1.com", "Site 1"),
                CreateWebSourceRow(2, "https://site2.com", "Site 2")));
        var sut = CreateSut(connection);

        var results = await sut.GetAllWebSourcesAsync();

        results.Should().HaveCount(2);
        results[0].Id.Should().Be(1);
        results[0].Name.Should().Be("Site 1");
        results[1].Id.Should().Be(2);
        results[1].Name.Should().Be("Site 2");
    }

    [Fact]
    public async Task GetAllWebSourcesAsync_ShouldReturnEmpty_WhenNoSources()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(CreateReader());
        var sut = CreateSut(connection);

        var results = await sut.GetAllWebSourcesAsync();

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllWebSourcesAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetAllWebSourcesAsync();

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Failed to get all web sources");
        exception.Which.InnerException.Should().Be(expected);
    }

    [Fact]
    public async Task UpdateWebSourceAsync_ShouldThrowArgumentNullException_WhenWebSourceIsNull()
    {
        var sut = CreateSut();

        var act = async () => await sut.UpdateWebSourceAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("webSource");
    }

    [Fact]
    public async Task UpdateWebSourceAsync_ShouldReturnTrue_WhenRowsAffected()
    {
        var webSource = CreateWebSource(10);
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(
            1,
            command =>
            {
                command.CommandText.Should().Contain("UPDATE [dbo].[WebSources]");
                command.Parameters["Id"].Should().Be(10);
                command.Parameters["Url"].Should().Be(webSource.Url);
                command.Parameters["Name"].Should().Be(webSource.Name);
            });
        var sut = CreateSut(connection);

        var result = await sut.UpdateWebSourceAsync(webSource);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateWebSourceAsync_ShouldReturnFalse_WhenNoRowsAffected()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(0);
        var sut = CreateSut(connection);

        var result = await sut.UpdateWebSourceAsync(CreateWebSource(99));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateWebSourceAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.UpdateWebSourceAsync(CreateWebSource(5));

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Failed to update web source with ID 5");
        exception.Which.InnerException.Should().Be(expected);
    }

    [Fact]
    public async Task DeleteWebSourceAsync_ShouldReturnTrue_WhenRowsAffected()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(
            1,
            command =>
            {
                command.Parameters["WebSourceId"].Should().Be(5);
            });
        var sut = CreateSut(connection);

        var result = await sut.DeleteWebSourceAsync(5);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteWebSourceAsync_ShouldReturnFalse_WhenNoRowsAffected()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(0);
        var sut = CreateSut(connection);

        var result = await sut.DeleteWebSourceAsync(999);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteWebSourceAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.DeleteWebSourceAsync(5);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Failed to delete web source with ID 5");
        exception.Which.InnerException.Should().Be(expected);
    }

    [Fact]
    public async Task GetWebSourceByUrlAsync_ShouldThrowArgumentNullException_WhenUrlIsNull()
    {
        var sut = CreateSut();

        var act = async () => await sut.GetWebSourceByUrlAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("url");
    }

    [Fact]
    public async Task GetWebSourceByUrlAsync_ShouldReturnWebSource_WhenFound()
    {
        var uri = new Uri("https://example.com");
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            CreateReader(CreateWebSourceRow(5, "https://example.com", "Example")),
            command =>
            {
                command.Parameters["Url"].Should().Be("https://example.com/");
            });
        var sut = CreateSut(connection);

        var result = await sut.GetWebSourceByUrlAsync(uri);

        result.Should().NotBeNull();
        result!.Id.Should().Be(5);
        result.Url.Should().Be("https://example.com");
    }

    [Fact]
    public async Task GetWebSourceByUrlAsync_ShouldReturnNull_WhenNotFound()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(CreateReader());
        var sut = CreateSut(connection);

        var result = await sut.GetWebSourceByUrlAsync(new Uri("https://notfound.com"));

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetWebSourceByUrlAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetWebSourceByUrlAsync(new Uri("https://example.com"));

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Failed to get web source by URL https://example.com/");
        exception.Which.InnerException.Should().Be(expected);
    }

    private static WebSourceRepository CreateSut(FakeDbConnection? connection = null)
    {
        var factory = new Mock<ISqlConnectionFactory>();
        factory
            .Setup(x => x.CreateOpenConnectionAsync())
            .ReturnsAsync(connection ?? new FakeDbConnection());
        return new WebSourceRepository(factory.Object, NullLogger<WebSourceRepository>.Instance);
    }

    private static WebSourceRepository CreateThrowingSut(Exception exception)
    {
        var factory = new Mock<ISqlConnectionFactory>();
        factory.Setup(x => x.CreateOpenConnectionAsync()).ThrowsAsync(exception);
        return new WebSourceRepository(factory.Object, NullLogger<WebSourceRepository>.Instance);
    }

    private static WebSource CreateWebSource(int id = 0) => new()
    {
        Id = id,
        Url = "https://example.com",
        Name = "Example Site",
        Description = "Test web source",
        IsEnabled = true,
        TrustTier = 3,
        CreatedDate = new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc),
        LastUpdatedDate = null,
        LastCrawledDate = null,
        CrawlFrequencyHours = 24,
        IncludeInSearch = true,
        MaxCrawlDepth = 2
    };

    private static Dictionary<string, object?> CreateWebSourceRow(int id, string url, string name) => new()
    {
        ["Id"] = id,
        ["Url"] = url,
        ["Name"] = name,
        ["Description"] = "Test web source",
        ["IsEnabled"] = true,
        ["TrustTier"] = 3,
        ["CreatedDate"] = new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc),
        ["LastUpdatedDate"] = DBNull.Value,
        ["LastCrawledDate"] = DBNull.Value,
        ["CrawlFrequencyHours"] = 24,
        ["IncludeInSearch"] = true,
        ["MaxCrawlDepth"] = 2
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
            => new FakeDbTransaction(this, isolationLevel);

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
        public override void Commit() { }
        public override void Rollback() { }
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
