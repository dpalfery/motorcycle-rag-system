using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.DbSetup;

namespace MotorcycleRAG.DbSetup.Tests;

public class DbSetupConnectionFactoryTests
{
    [Fact]
    public void Create_WithConnectionString_ReturnsConfiguredConnection()
    {
        // Arrange
        var factory = new SqlDbSetupConnectionFactory();
        const string connectionString = "Server=localhost;Database=MotorcycleRAG;Integrated Security=true";

        // Act
        using var connection = factory.Create(connectionString);

        // Assert
        connection.ConnectionString.Should().Be(connectionString);
    }

    [Fact]
    public async Task DatabaseExistsAsync_WithFakeConnection_UsesInjectedFactory()
    {
        // Arrange
        var factory = new RecordingDbSetupConnectionFactory { ScalarResult = 42 };
        var sut = new SqlServerProvisioner(NullLogger<SqlServerProvisioner>.Instance, factory);

        // Act
        var exists = await sut.DatabaseExistsAsync("fake-connection", "MotorcycleRAG");

        // Assert
        exists.Should().BeTrue();
        factory.ConnectionStrings.Should().ContainSingle().Which.Should().Be("fake-connection");
        var command = factory.Connections.Single().ExecutedCommands.Should().ContainSingle().Which;
        command.CommandText.Should().Contain("sys.databases");
        command.Parameters.TryGetValue("@dbName", out var databaseName).Should().BeTrue();
        databaseName.Should().Be("MotorcycleRAG");
    }

    [Fact]
    public async Task PerformPreflightChecksAsync_WithFakeConnections_UsesInjectedFactoryForEveryCheck()
    {
        // Arrange
        var factory = new RecordingDbSetupConnectionFactory { ScalarResult = "16.0" };
        var sut = new PreflightChecker(NullLogger<PreflightChecker>.Instance, factory);

        // Act
        var passed = await sut.PerformPreflightChecksAsync("fake-connection", "MotorcycleRAG");

        // Assert
        passed.Should().BeTrue();
        factory.ConnectionStrings.Should().HaveCount(2).And.OnlyContain(value => value == "fake-connection");
        factory.Connections.SelectMany(connection => connection.ExecutedCommands)
            .Should().HaveCount(2);
    }

    [Fact]
    public async Task ExecuteScriptFileAsync_WithFakeConnection_UsesInjectedFactoryWithoutNetworkAccess()
    {
        // Arrange
        var factory = new RecordingDbSetupConnectionFactory();
        var sut = new SqlScriptExecutor(NullLogger<SqlScriptExecutor>.Instance, factory);
        var scriptPath = Path.GetTempFileName();
        await File.WriteAllTextAsync(scriptPath, "SELECT 1;\nGO\nSELECT 2;");

        try
        {
            // Act
            var succeeded = await sut.ExecuteScriptFileAsync("fake-connection", scriptPath);

            // Assert
            succeeded.Should().BeTrue();
            factory.ConnectionStrings.Should().ContainSingle().Which.Should().Be("fake-connection");
            factory.Connections.Single().ExecutedCommands.Should().HaveCount(2);
        }
        finally
        {
            File.Delete(scriptPath);
        }
    }

    private sealed class RecordingDbSetupConnectionFactory : IDbSetupConnectionFactory
    {
        public List<string> ConnectionStrings { get; } = [];

        public List<RecordingDbConnection> Connections { get; } = [];

        public object? ScalarResult { get; init; } = 1;

        public DbConnection Create(string connectionString)
        {
            var connection = new RecordingDbConnection(ScalarResult);
            ConnectionStrings.Add(connectionString);
            Connections.Add(connection);
            return connection;
        }
    }

    private sealed class RecordingDbConnection : DbConnection
    {
        private readonly object? _scalarResult;

        public RecordingDbConnection(object? scalarResult)
        {
            _scalarResult = scalarResult;
        }

        public List<RecordedCommand> ExecutedCommands { get; } = [];

        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;

        public override string Database => "Fake";
        public override string DataSource => "Fake";
        public override string ServerVersion => "1.0";
        public override ConnectionState State => ConnectionState.Open;

        public override void ChangeDatabase(string databaseName) { }
        public override void Close() { }
        public override void Open() { }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
            throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => new RecordingDbCommand(this, _scalarResult);

        internal void Record(RecordingDbCommand command)
        {
            ExecutedCommands.Add(new RecordedCommand(
                command.CommandText,
                command.Parameters.Cast<DbParameter>().ToDictionary(
                    parameter => parameter.ParameterName,
                    parameter => parameter.Value,
                    StringComparer.Ordinal)));
        }
    }

    private sealed class RecordingDbCommand : DbCommand
    {
        private readonly RecordingDbConnection _connection;
        private readonly object? _scalarResult;
        private readonly RecordingDbParameterCollection _parameters = new();

        public RecordingDbCommand(RecordingDbConnection connection, object? scalarResult)
        {
            _connection = connection;
            _scalarResult = scalarResult;
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

        public override void Cancel() { }

        public override int ExecuteNonQuery()
        {
            _connection.Record(this);
            return 1;
        }

        public override object? ExecuteScalar()
        {
            _connection.Record(this);
            return _scalarResult;
        }

        public override void Prepare() { }

        protected override DbParameter CreateDbParameter() => new RecordingDbParameter();

        [SuppressMessage("Reliability", "CA2000", Justification = "The reader owns the in-memory table for this short-lived unit-test command.")]
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            _connection.Record(this);

            var table = new DataTable();
            table.Columns.AddRange([
                new DataColumn("IsSysAdmin", typeof(int)),
                new DataColumn("IsServerAdmin", typeof(int)),
                new DataColumn("CanCreateDatabase", typeof(int)),
                new DataColumn("CanAlterLogin", typeof(int))
            ]);
            table.Rows.Add(1, 0, 0, 0);
            return table.CreateDataReader();
        }
    }

    private sealed class RecordingDbParameterCollection : DbParameterCollection
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

    private sealed class RecordingDbParameter : DbParameter
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

    private sealed record RecordedCommand(string CommandText, IReadOnlyDictionary<string, object?> Parameters);
}
