using System.Collections;
using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Persistence.Sql;
using MotorcycleRAG.Persistence.Sql.Repositories;

namespace MotorcycleRAG.UnitTests.Persistence.Sql.Repositories;

public sealed class UserIdentityRepositoryTests
{
    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenConnectionFactoryIsNull()
    {
        var act = () => new UserIdentityRepository(null!, NullLogger<UserIdentityRepository>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        var factory = new Mock<ISqlConnectionFactory>();

        var act = () => new UserIdentityRepository(factory.Object, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task GetManagedUserIdAsync_ShouldThrowArgumentException_WhenEmailIsBlank(string? email)
    {
        var sut = CreateSut();

        var act = async () => await sut.GetManagedUserIdAsync("issuer", "sub", email!, IdentityProvider.Microsoft);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("email");
    }

    [Fact]
    public async Task GetManagedUserIdAsync_ShouldReturnManagedUserId_WhenFound()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueScalar(
            "managed-user-42",
            command =>
            {
                command.CommandText.Should().Contain("SELECT TOP (1) [ManagedUserId]");
                command.Parameters["Email"].Should().Be("rider@example.com");
                command.Parameters["Provider"].Should().Be(IdentityProvider.Microsoft.ToString());
                command.Parameters["Issuer"].Should().Be("https://login.microsoftonline.com/tenant/v2.0");
                command.Parameters["Subject"].Should().Be("subject-1");
            });
        var sut = CreateSut(connection);

        var result = await sut.GetManagedUserIdAsync(
            "https://login.microsoftonline.com/tenant/v2.0",
            "subject-1",
            "rider@example.com",
            IdentityProvider.Microsoft);

        result.Should().Be("managed-user-42");
    }

    [Fact]
    public async Task GetManagedUserIdAsync_ShouldReturnNull_WhenNotFound()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueScalar(null);
        var sut = CreateSut(connection);

        var result = await sut.GetManagedUserIdAsync("issuer", "sub", "rider@example.com", IdentityProvider.Google);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetManagedUserIdAsync_ShouldHandleNullIssuerAndSubject()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueScalar(
            "managed-user-1",
            command =>
            {
                command.Parameters["Issuer"].Should().Be(DBNull.Value);
                command.Parameters["Subject"].Should().Be(DBNull.Value);
            });
        var sut = CreateSut(connection);

        var result = await sut.GetManagedUserIdAsync("", "", "rider@example.com", IdentityProvider.Microsoft);

        result.Should().Be("managed-user-1");
    }

    [Fact]
    public async Task GetManagedUserIdAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetManagedUserIdAsync("issuer", "sub", "rider@example.com", IdentityProvider.Microsoft);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Failed to resolve identity for rider@example.com");
        exception.Which.InnerException.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task GetActiveByManagedUserIdAsync_ShouldThrowArgumentException_WhenManagedUserIdIsBlank(string? managedUserId)
    {
        var sut = CreateSut();

        var act = async () => await sut.GetActiveByManagedUserIdAsync(managedUserId!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("managedUserId");
    }

    [Fact]
    public async Task GetActiveByManagedUserIdAsync_ShouldReturnMappedRecord_WhenFound()
    {
        var managedUserId = "managed-user-42";
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            CreateReader(new Dictionary<string, object?>
            {
                ["ManagedUserId"] = managedUserId,
                ["Provider"] = "Microsoft",
                ["ProviderEmail"] = "rider@example.com",
                ["ExternalDirectoryObjectId"] = "ext-obj-1"
            }),
            command =>
            {
                command.Parameters["ManagedUserId"].Should().Be(managedUserId);
            });
        var sut = CreateSut(connection);

        var result = await sut.GetActiveByManagedUserIdAsync(managedUserId);

        result.Should().NotBeNull();
        result!.ManagedUserId.Should().Be(managedUserId);
        result.Provider.Should().Be(IdentityProvider.Microsoft);
        result.ProviderEmail.Should().Be("rider@example.com");
        result.ExternalDirectoryObjectId.Should().Be("ext-obj-1");
    }

    [Fact]
    public async Task GetActiveByManagedUserIdAsync_ShouldReturnNull_WhenNotActive()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(CreateReader());
        var sut = CreateSut(connection);

        var result = await sut.GetActiveByManagedUserIdAsync("managed-user-42");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetActiveByManagedUserIdAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetActiveByManagedUserIdAsync("managed-user-42");

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Failed to get identity link for managed-user-42");
        exception.Which.InnerException.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task ExistsAsync_ShouldThrowArgumentException_WhenManagedUserIdIsBlank(string? managedUserId)
    {
        var sut = CreateSut();

        var act = async () => await sut.ExistsAsync(managedUserId!, IdentityProvider.Microsoft, "rider@example.com");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("managedUserId");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task ExistsAsync_ShouldThrowArgumentException_WhenProviderEmailIsBlank(string? email)
    {
        var sut = CreateSut();

        var act = async () => await sut.ExistsAsync("managed-user-1", IdentityProvider.Microsoft, email!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("providerEmail");
    }

    [Fact]
    public async Task ExistsAsync_ShouldReturnTrue_WhenCountPositive()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueScalar(
            2,
            command =>
            {
                command.Parameters["ManagedUserId"].Should().Be("managed-user-1");
                command.Parameters["Provider"].Should().Be(IdentityProvider.Microsoft.ToString());
                command.Parameters["ProviderEmail"].Should().Be("rider@example.com");
            });
        var sut = CreateSut(connection);

        var result = await sut.ExistsAsync("managed-user-1", IdentityProvider.Microsoft, "rider@example.com");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_ShouldReturnFalse_WhenCountZero()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueScalar(0);
        var sut = CreateSut(connection);

        var result = await sut.ExistsAsync("managed-user-1", IdentityProvider.Google, "rider@example.com");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.ExistsAsync("managed-user-1", IdentityProvider.Microsoft, "rider@example.com");

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Failed to check identity link for managed-user-1");
        exception.Which.InnerException.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task UpsertAsync_ShouldThrowArgumentException_WhenManagedUserIdIsBlank(string? managedUserId)
    {
        var sut = CreateSut();

        var act = async () => await sut.UpsertAsync(
            managedUserId!, IdentityProvider.Microsoft, "rider@example.com",
            null, null, null, null);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("managedUserId");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task UpsertAsync_ShouldThrowArgumentException_WhenProviderEmailIsBlank(string? email)
    {
        var sut = CreateSut();

        var act = async () => await sut.UpsertAsync(
            "managed-user-1", IdentityProvider.Microsoft, email!,
            null, null, null, null);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("providerEmail");
    }

    [Fact]
    public async Task UpsertAsync_ShouldReturnTrue_WhenRowsAffected()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(
            1,
            command =>
            {
                command.Parameters["ManagedUserId"].Should().Be("managed-user-1");
                command.Parameters["Provider"].Should().Be(IdentityProvider.Google.ToString());
                command.Parameters["ProviderEmail"].Should().Be("rider@example.com");
                command.Parameters["Issuer"].Should().Be("issuer");
                command.Parameters["Subject"].Should().Be("subject");
                command.Parameters["ProviderUserId"].Should().Be("prov-user-id");
                command.Parameters["ExternalDirectoryObjectId"].Should().Be("ext-obj-id");
            });
        var sut = CreateSut(connection);

        var result = await sut.UpsertAsync(
            "managed-user-1", IdentityProvider.Google, "rider@example.com",
            "issuer", "subject", "prov-user-id", "ext-obj-id");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task UpsertAsync_ShouldReturnFalse_WhenNoRowsAffected()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(0);
        var sut = CreateSut(connection);

        var result = await sut.UpsertAsync(
            "managed-user-1", IdentityProvider.Microsoft, "rider@example.com",
            "issuer", "sub", null, null);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpsertAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.UpsertAsync(
            "managed-user-1", IdentityProvider.Microsoft, "rider@example.com",
            null, null, null, null);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Failed to upsert identity link for managed-user-1");
        exception.Which.InnerException.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task MarkAccessRevokedAsync_ShouldThrowArgumentException_WhenManagedUserIdIsBlank(string? managedUserId)
    {
        var sut = CreateSut();

        var act = async () => await sut.MarkAccessRevokedAsync(managedUserId!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("managedUserId");
    }

    [Fact]
    public async Task MarkAccessRevokedAsync_ShouldReturnTrue_WhenRowsAffected()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(
            1,
            command =>
            {
                command.Parameters["ManagedUserId"].Should().Be("managed-user-1");
            });
        var sut = CreateSut(connection);

        var result = await sut.MarkAccessRevokedAsync("managed-user-1");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task MarkAccessRevokedAsync_ShouldReturnFalse_WhenNoRowsAffected()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(0);
        var sut = CreateSut(connection);

        var result = await sut.MarkAccessRevokedAsync("managed-user-1");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task MarkAccessRevokedAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.MarkAccessRevokedAsync("managed-user-1");

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Failed to revoke identity links for managed-user-1");
        exception.Which.InnerException.Should().Be(expected);
    }

    private static UserIdentityRepository CreateSut(FakeDbConnection? connection = null)
    {
        var factory = new Mock<ISqlConnectionFactory>();
        factory
            .Setup(x => x.CreateOpenConnectionAsync())
            .ReturnsAsync(connection ?? new FakeDbConnection());
        return new UserIdentityRepository(factory.Object, NullLogger<UserIdentityRepository>.Instance);
    }

    private static UserIdentityRepository CreateThrowingSut(Exception exception)
    {
        var factory = new Mock<ISqlConnectionFactory>();
        factory.Setup(x => x.CreateOpenConnectionAsync()).ThrowsAsync(exception);
        return new UserIdentityRepository(factory.Object, NullLogger<UserIdentityRepository>.Instance);
    }

    private static DbDataReader CreateReader(params IReadOnlyDictionary<string, object?>[] rows)
    {
        var table = new DataTable();
        if (rows.Length == 0)
        {
            table.Columns.Add("ManagedUserId", typeof(object));
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
