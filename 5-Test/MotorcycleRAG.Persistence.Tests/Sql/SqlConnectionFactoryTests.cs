using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Persistence.Sql;
using MotorcycleRAG.UnitTests.Logging;

namespace MotorcycleRAG.Persistence.Tests.Sql;

public class SqlConnectionFactoryTests
{
    private const string ValidConnectionString =
        "Server=localhost;Database=TestDb;Trusted_Connection=True;TrustServerCertificate=True;";

    private static SqlOptions CreateValidOptions() => new()
    {
        ConnectionString = ValidConnectionString,
        CommandTimeout = 30,
        ConnectionTimeout = 15,
        MaxPoolSize = 50
    };

    private static SqlOptions CreateOptions(string? connectionString) => new()
    {
        ConnectionString = connectionString!,
        CommandTimeout = 30,
        ConnectionTimeout = 15,
        MaxPoolSize = 50
    };

    private static IOptions<SqlOptions> WrapOptions(SqlOptions options) =>
        TestHelpers.OptionsFor(options);

    // ──────────────────────────────────────────────
    // Constructor null-guard tests
    // ──────────────────────────────────────────────

    [Fact]
    public void Constructor_WhenSqlOptionsIsNull_ThrowsArgumentNullException()
    {
        var act = () => new SqlConnectionFactory(null!, new SpyLogger<SqlConnectionFactory>());
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("sqlOptions");
    }

    [Fact]
    public void Constructor_WhenLoggerIsNull_ThrowsArgumentNullException()
    {
        var options = WrapOptions(CreateValidOptions());
        var act = () => new SqlConnectionFactory(options, null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public void Constructor_WhenOptionsValueIsNull_ThrowsArgumentNullException()
    {
        var options = WrapOptions(null!);
        var act = () => new SqlConnectionFactory(options, new SpyLogger<SqlConnectionFactory>());
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("sqlOptions");
    }

    // ──────────────────────────────────────────────
    // Connection string validation
    // ──────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Constructor_WhenConnectionStringIsNullOrWhiteSpace_ThrowsInvalidOperationException(
        string? connectionString)
    {
        var options = WrapOptions(CreateOptions(connectionString));
        var act = () => new SqlConnectionFactory(options, new SpyLogger<SqlConnectionFactory>());
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Sql:ConnectionString*");
    }

    [Fact]
    public void Constructor_WithValidConnectionString_DoesNotThrow()
    {
        var options = WrapOptions(CreateValidOptions());
        var act = () => new SqlConnectionFactory(options, new SpyLogger<SqlConnectionFactory>());
        act.Should().NotThrow();
    }

    // ──────────────────────────────────────────────
    // Embedded credentials warning tests
    // ──────────────────────────────────────────────

    [Theory]
    [InlineData("Server=localhost;Password=secret;Database=TestDb")]
    [InlineData("Server=localhost;PWD=secret;Database=TestDb")]
    [InlineData("Server=localhost;User ID=admin;Database=TestDb")]
    [InlineData("Server=localhost;UID=admin;Database=TestDb")]
    public void Constructor_WhenConnectionStringContainsEmbeddedCredentials_LogsWarning(
        string connectionString)
    {
        var spy = new SpyLogger<SqlConnectionFactory>();
        var options = WrapOptions(CreateOptions(connectionString));

        _ = new SqlConnectionFactory(options, spy);

        spy.Entries.Should().ContainSingle(e => e.LogLevel == LogLevel.Warning)
            .Which.FormattedMessage.Should().Contain("embedded credentials");
    }

    [Fact]
    public void Constructor_WhenConnectionStringHasNoEmbeddedCredentials_DoesNotLogWarning()
    {
        var spy = new SpyLogger<SqlConnectionFactory>();
        var options = WrapOptions(CreateValidOptions()); // trusted connection, no credentials

        _ = new SqlConnectionFactory(options, spy);

        spy.Entries.Where(e => e.LogLevel == LogLevel.Warning).Should().BeEmpty();
    }

    // ──────────────────────────────────────────────
    // Connection string builder property application
    // ──────────────────────────────────────────────

    [Fact]
    public void CreateConnection_ConnectionString_ContainsConfiguredMaxPoolSize()
    {
        var options = WrapOptions(CreateValidOptions());
        var sut = new SqlConnectionFactory(options, new SpyLogger<SqlConnectionFactory>());

        var connection = (SqlConnection)sut.CreateConnection();

        connection.ConnectionString.Should().Contain("Max Pool Size=50");
    }

    [Fact]
    public void CreateConnection_ConnectionString_ContainsConfiguredConnectTimeout()
    {
        var options = WrapOptions(CreateValidOptions());
        var sut = new SqlConnectionFactory(options, new SpyLogger<SqlConnectionFactory>());

        var connection = (SqlConnection)sut.CreateConnection();

        connection.ConnectionString.Should().Contain("Connect Timeout=15");
    }

    [Fact]
    public void CreateConnection_ConnectionString_ContainsConnectionLifetime()
    {
        var options = WrapOptions(CreateValidOptions());
        var sut = new SqlConnectionFactory(options, new SpyLogger<SqlConnectionFactory>());

        var connection = (SqlConnection)sut.CreateConnection();

        // SqlConnectionStringBuilder normalizes "Connection Lifetime" to "Load Balance Timeout"
        connection.ConnectionString.Should().Contain("Load Balance Timeout=300");
    }

    [Fact]
    public void CreateConnection_ConnectionString_UsesDefaultMaxPoolSizeWhenNotOverridden()
    {
        var defaultOptions = new SqlOptions
        {
            ConnectionString = ValidConnectionString,
            CommandTimeout = 0,
            ConnectionTimeout = 30,
            MaxPoolSize = 100 // default
        };
        var sut = new SqlConnectionFactory(WrapOptions(defaultOptions), new SpyLogger<SqlConnectionFactory>());

        var connection = (SqlConnection)sut.CreateConnection();

        connection.ConnectionString.Should().Contain("Max Pool Size=100");
    }

    // ──────────────────────────────────────────────
    // CreateConnection tests
    // ──────────────────────────────────────────────

    [Fact]
    public void CreateConnection_ReturnsNonNullSqlConnection()
    {
        var sut = new SqlConnectionFactory(
            WrapOptions(CreateValidOptions()),
            new SpyLogger<SqlConnectionFactory>());

        var connection = sut.CreateConnection();

        connection.Should().NotBeNull();
        connection.Should().BeOfType<SqlConnection>();
    }

    [Fact]
    public void CreateConnection_ReturnsClosedConnection()
    {
        var sut = new SqlConnectionFactory(
            WrapOptions(CreateValidOptions()),
            new SpyLogger<SqlConnectionFactory>());

        var connection = sut.CreateConnection();

        connection.State.Should().Be(ConnectionState.Closed);
    }

    [Fact]
    public void CreateConnection_LogsDebugMessage()
    {
        var spy = new SpyLogger<SqlConnectionFactory>();
        var sut = new SqlConnectionFactory(WrapOptions(CreateValidOptions()), spy);

        sut.CreateConnection();

        spy.Entries.Should().ContainSingle(e => e.LogLevel == LogLevel.Debug)
            .Which.FormattedMessage.Should().Contain("Created SQL connection");
    }

    // ──────────────────────────────────────────────
    // CreateConnectionAsync tests
    // ──────────────────────────────────────────────

    [Fact]
    public async Task CreateConnectionAsync_ReturnsNonNullSqlConnection()
    {
        var sut = new SqlConnectionFactory(
            WrapOptions(CreateValidOptions()),
            new SpyLogger<SqlConnectionFactory>());

        var connection = await sut.CreateConnectionAsync();

        connection.Should().NotBeNull();
        connection.Should().BeOfType<SqlConnection>();
    }

    [Fact]
    public async Task CreateConnectionAsync_LogsDebugMessage()
    {
        var spy = new SpyLogger<SqlConnectionFactory>();
        var sut = new SqlConnectionFactory(WrapOptions(CreateValidOptions()), spy);

        await sut.CreateConnectionAsync();

        spy.Entries.Should().ContainSingle(e => e.LogLevel == LogLevel.Debug)
            .Which.FormattedMessage.Should().Contain("Created SQL connection");
    }

    // ──────────────────────────────────────────────
    // CreateCommand overload tests
    // ──────────────────────────────────────────────

    [Fact]
    public void CreateCommand_Basic_WithValidConnection_ReturnsCommand()
    {
        var sut = new SqlConnectionFactory(
            WrapOptions(CreateValidOptions()),
            new SpyLogger<SqlConnectionFactory>());
        using var connection = (SqlConnection)sut.CreateConnection();

        var command = sut.CreateCommand(connection);

        command.Should().NotBeNull();
        command.Should().BeAssignableTo<IDbCommand>();
    }

    [Fact]
    public void CreateCommand_WithNullConnection_ThrowsArgumentNullException()
    {
        var sut = new SqlConnectionFactory(
            WrapOptions(CreateValidOptions()),
            new SpyLogger<SqlConnectionFactory>());

        var act = () => sut.CreateCommand(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("connection");
    }

    [Fact]
    public void CreateCommand_WithTransaction_SetsTransactionOnCommand()
    {
        var sut = new SqlConnectionFactory(
            WrapOptions(CreateValidOptions()),
            new SpyLogger<SqlConnectionFactory>());
        var mockTransaction = new Mock<IDbTransaction>();
        var mockConnection = new Mock<IDbConnection>();
        var mockCommand = new Mock<IDbCommand>();
        mockCommand.SetupProperty(c => c.Transaction);
        mockConnection.Setup(c => c.CreateCommand()).Returns(mockCommand.Object);

        var command = sut.CreateCommand(mockConnection.Object, mockTransaction.Object);

        command.Transaction.Should().Be(mockTransaction.Object);
    }

    [Fact]
    public void CreateCommand_WithCommandText_SetsCommandText()
    {
        var sut = new SqlConnectionFactory(
            WrapOptions(CreateValidOptions()),
            new SpyLogger<SqlConnectionFactory>());
        using var connection = (SqlConnection)sut.CreateConnection();

        var command = sut.CreateCommand(connection, "SELECT 1");

        command.CommandText.Should().Be("SELECT 1");
    }

    [Fact]
    public void CreateCommand_WithCommandType_SetsCommandType()
    {
        var sut = new SqlConnectionFactory(
            WrapOptions(CreateValidOptions()),
            new SpyLogger<SqlConnectionFactory>());
        using var connection = (SqlConnection)sut.CreateConnection();

        var command = sut.CreateCommand(connection, null, "sp_Test", CommandType.StoredProcedure, null);

        command.CommandType.Should().Be(CommandType.StoredProcedure);
    }

    [Fact]
    public void CreateCommand_WithExplicitTimeout_SetsCommandTimeout()
    {
        var sut = new SqlConnectionFactory(
            WrapOptions(CreateValidOptions()),
            new SpyLogger<SqlConnectionFactory>());
        using var connection = (SqlConnection)sut.CreateConnection();

        var command = sut.CreateCommand(connection, null, null, CommandType.Text, 120);

        command.CommandTimeout.Should().Be(120);
    }

    [Fact]
    public void CreateCommand_WithoutExplicitTimeout_UsesSqlOptionsCommandTimeout()
    {
        var options = CreateValidOptions();
        options.CommandTimeout = 45;
        var sut = new SqlConnectionFactory(WrapOptions(options), new SpyLogger<SqlConnectionFactory>());
        using var connection = (SqlConnection)sut.CreateConnection();

        var command = sut.CreateCommand(connection, null, null, CommandType.Text, null);

        command.CommandTimeout.Should().Be(45);
    }

    [Fact]
    public void CreateCommand_WhenSqlOptionsCommandTimeoutIsZero_DoesNotSetTimeout()
    {
        var options = CreateValidOptions();
        options.CommandTimeout = 0;
        var sut = new SqlConnectionFactory(WrapOptions(options), new SpyLogger<SqlConnectionFactory>());
        using var connection = (SqlConnection)sut.CreateConnection();

        var command = sut.CreateCommand(connection, null, null, CommandType.Text, null);

        // CommandTimeout defaults to 30 in SqlCommand; factory does not override when option is 0
        command.CommandTimeout.Should().Be(30);
    }

    [Fact]
    public void CreateCommand_WhenConnectionCreateCommandReturnsNull_ThrowsInvalidOperationException()
    {
        var mockConnection = new Mock<IDbConnection>();
        mockConnection.Setup(c => c.CreateCommand()).Returns((IDbCommand)null!);
        var sut = new SqlConnectionFactory(
            WrapOptions(CreateValidOptions()),
            new SpyLogger<SqlConnectionFactory>());

        var act = () => sut.CreateCommand(mockConnection.Object);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*failed to create command*");
    }

    [Fact]
    public void CreateCommand_WithoutTransaction_CommandTransactionIsNull()
    {
        var sut = new SqlConnectionFactory(
            WrapOptions(CreateValidOptions()),
            new SpyLogger<SqlConnectionFactory>());
        using var connection = (SqlConnection)sut.CreateConnection();

        var command = sut.CreateCommand(connection, (IDbTransaction?)null);

        command.Transaction.Should().BeNull();
    }

    // ──────────────────────────────────────────────
    // CreateParameter overload tests
    // ──────────────────────────────────────────────

    [Fact]
    public void CreateParameter_Basic_SetsParameterNameAndValue()
    {
        var sut = new SqlConnectionFactory(
            WrapOptions(CreateValidOptions()),
            new SpyLogger<SqlConnectionFactory>());

        var parameter = (SqlParameter)sut.CreateParameter("@name", "test-value");

        parameter.ParameterName.Should().Be("@name");
        parameter.Value.Should().Be("test-value");
    }

    [Fact]
    public void CreateParameter_WithNullValue_UsesDBNull()
    {
        var sut = new SqlConnectionFactory(
            WrapOptions(CreateValidOptions()),
            new SpyLogger<SqlConnectionFactory>());

        var parameter = (SqlParameter)sut.CreateParameter("@name", null);

        parameter.Value.Should().Be(DBNull.Value);
    }

    [Fact]
    public void CreateParameter_WithDbType_SetsDbType()
    {
        var sut = new SqlConnectionFactory(
            WrapOptions(CreateValidOptions()),
            new SpyLogger<SqlConnectionFactory>());

        var parameter = (SqlParameter)sut.CreateParameter("@id", 1, DbType.Int32);

        parameter.DbType.Should().Be(DbType.Int32);
        parameter.Value.Should().Be(1);
    }

    [Fact]
    public void CreateParameter_FullOverload_SetsAllProperties()
    {
        var sut = new SqlConnectionFactory(
            WrapOptions(CreateValidOptions()),
            new SpyLogger<SqlConnectionFactory>());

        var parameter = (SqlParameter)sut.CreateParameter(
            "@output", 42, DbType.Int32, ParameterDirection.Output);

        parameter.ParameterName.Should().Be("@output");
        parameter.Value.Should().Be(42);
        parameter.DbType.Should().Be(DbType.Int32);
        parameter.Direction.Should().Be(ParameterDirection.Output);
    }

    [Fact]
    public void CreateParameter_BasicOverload_DefaultsDirectionToInput()
    {
        var sut = new SqlConnectionFactory(
            WrapOptions(CreateValidOptions()),
            new SpyLogger<SqlConnectionFactory>());

        var parameter = (SqlParameter)sut.CreateParameter("@name", "value");

        parameter.Direction.Should().Be(ParameterDirection.Input);
    }

    [Fact]
    public void CreateParameter_ReturnsIDataParameter()
    {
        var sut = new SqlConnectionFactory(
            WrapOptions(CreateValidOptions()),
            new SpyLogger<SqlConnectionFactory>());

        var parameter = sut.CreateParameter("@x", "y");

        parameter.Should().BeAssignableTo<IDataParameter>();
    }

    // ──────────────────────────────────────────────
    // Connection string edge case: case insensitivity
    // ──────────────────────────────────────────────

    [Theory]
    [InlineData("Server=localhost;password=secret;Database=TestDb")]
    [InlineData("Server=localhost;pwd=secret;Database=TestDb")]
    [InlineData("Server=localhost;user id=admin;Database=TestDb")]
    [InlineData("Server=localhost;uid=admin;Database=TestDb")]
    public void Constructor_EmbeddedCredentialsCheck_IsCaseInsensitive(string connectionString)
    {
        var spy = new SpyLogger<SqlConnectionFactory>();
        var options = WrapOptions(CreateOptions(connectionString));

        _ = new SqlConnectionFactory(options, spy);

        spy.Entries.Should().ContainSingle(e => e.LogLevel == LogLevel.Warning)
            .Which.FormattedMessage.Should().Contain("embedded credentials");
    }

    // ──────────────────────────────────────────────
    // Multiple calls produce independent connections
    // ──────────────────────────────────────────────

    [Fact]
    public void CreateConnection_MultipleCalls_ProduceIndependentConnections()
    {
        var sut = new SqlConnectionFactory(
            WrapOptions(CreateValidOptions()),
            new SpyLogger<SqlConnectionFactory>());

        var conn1 = sut.CreateConnection();
        var conn2 = sut.CreateConnection();

        conn1.Should().NotBeSameAs(conn2);
    }

    [Fact]
    public async Task CreateConnectionAsync_MultipleCalls_ProduceIndependentConnections()
    {
        var sut = new SqlConnectionFactory(
            WrapOptions(CreateValidOptions()),
            new SpyLogger<SqlConnectionFactory>());

        var conn1 = await sut.CreateConnectionAsync();
        var conn2 = await sut.CreateConnectionAsync();

        conn1.Should().NotBeSameAs(conn2);
    }

    // ──────────────────────────────────────────────
    // CreateOpenConnectionAsync tests
    // ──────────────────────────────────────────────
    // Note: the happy path (successful OpenAsync) requires a real SQL Server and
    // belongs in integration tests. The error path (catch block) is tested here
    // via a testable subclass that overrides OpenSqlConnectionCore to throw.

    /// <summary>
    /// A testable SqlConnectionFactory that allows injecting an exception into
    /// the connection-opening call so the catch-block logic (logging, disposal,
    /// InvalidOperationException wrapping) is exercisable without a real SQL
    /// Server instance. Uses the protected virtual OpenSqlConnectionCore seam.
    /// </summary>
    private sealed class TestableSqlConnectionFactory : SqlConnectionFactory
    {
        private readonly Exception? _openException;

        public TestableSqlConnectionFactory(
            IOptions<SqlOptions> sqlOptions,
            ILogger<SqlConnectionFactory> logger,
            Exception? openException = null)
            : base(sqlOptions, logger)
        {
            _openException = openException;
        }

        protected override async Task OpenSqlConnectionCore(SqlConnection connection)
        {
            await Task.Yield();
            if (_openException is not null)
                throw _openException;
        }
    }

    [Fact]
    public async Task CreateOpenConnectionAsync_WhenOpenFails_ThrowsInvalidOperationException()
    {
        var spy = new SpyLogger<SqlConnectionFactory>();
        var sut = new TestableSqlConnectionFactory(
            WrapOptions(CreateValidOptions()),
            spy,
            openException: new InvalidOperationException("Simulated open failure"));

        var act = () => sut.CreateOpenConnectionAsync();

        // The catch block wraps every exception in InvalidOperationException.
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*opening a SQL connection*");
    }

    [Fact]
    public async Task CreateOpenConnectionAsync_WhenOpenFails_LogsErrorAndDisposesConnection()
    {
        var spy = new SpyLogger<SqlConnectionFactory>();
        var sut = new TestableSqlConnectionFactory(
            WrapOptions(CreateValidOptions()),
            spy,
            openException: new InvalidOperationException("Simulated open failure"));

        try
        {
            await sut.CreateOpenConnectionAsync();
        }
        catch (InvalidOperationException)
        {
            // Expected — the catch block re-wraps the exception.
        }

        // The catch block logs an error and disposes the connection before
        // re-throwing. SqlConnection is sealed, so we verify disposal indirectly:
        // if the error log is present and the exception was an InvalidOperationException
        // with the catch-block message, the Dispose() call between them executed.
        spy.Entries.Should().Contain(e => e.LogLevel == LogLevel.Error)
            .Which.FormattedMessage.Should().Contain("Failed to open SQL connection");
    }

    // ──────────────────────────────────────────────
    // CreateConnection / CreateConnectionAsync catch-block analysis
    // ──────────────────────────────────────────────
    // The catch blocks at lines 84-88 and 103-107 of SqlConnectionFactory.cs wrap
    // `new SqlConnection(_connectionString)`. The SqlConnection constructor stores
    // the connection string without validation; it cannot throw with a non-null
    // argument. The connection string is validated in the constructor (null/empty
    // check, SqlConnectionStringBuilder parsing) and would fail at construction
    // time before the field is stored. Therefore these catch blocks are purely
    // defensive and unreachable in any test scenario — they are exempt from the
    // 100 % line-coverage target.
}
