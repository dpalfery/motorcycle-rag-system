using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Persistence.Sql;

namespace MotorcycleRAG.API.Tests.Persistence.Sql.Helpers;

/// <summary>
/// A fake <see cref="DbConnection"/> intended for unit testing repository classes.
/// Plans (scalar, non-query, reader — including exception variants) are queued
/// ahead of each test. Every executed command is captured for assertion.
/// </summary>
/// <remarks>
/// This is the single shared implementation for all Sql/Repositories unit tests
/// in this project. Do not re-duplicate this class per test file — extend it
/// here instead if a repository needs a new fake behavior.
/// </remarks>
internal sealed class FakeDbConnection : DbConnection
{
    private readonly Queue<CommandPlan> _plans = new();

    // --- public test surface -------------------------------------------------

    public List<ExecutedCommand> ExecutedCommands { get; } = [];

    public int BeginTransactionCount { get; private set; }

    public FakeDbTransaction? LastTransaction { get; private set; }

    // --- enqueue helpers -----------------------------------------------------

    public void EnqueueScalar(object? result, Action<ExecutedCommand>? assert = null)
    {
        _plans.Enqueue(new CommandPlan(CommandKind.Scalar, () => result, assert));
    }

    public void EnqueueScalarException(Exception exception, Action<ExecutedCommand>? assert = null)
    {
        _plans.Enqueue(new CommandPlan(CommandKind.Scalar, () => throw exception, assert));
    }

    public void EnqueueNonQuery(int affectedRows, Action<ExecutedCommand>? assert = null)
    {
        _plans.Enqueue(new CommandPlan(CommandKind.NonQuery, () => affectedRows, assert));
    }

    public void EnqueueNonQueryException(Exception exception, Action<ExecutedCommand>? assert = null)
    {
        _plans.Enqueue(new CommandPlan(CommandKind.NonQuery, () => throw exception, assert));
    }

    public void EnqueueReader(DbDataReader reader, Action<ExecutedCommand>? assert = null)
    {
        _plans.Enqueue(new CommandPlan(CommandKind.Reader, () => reader, assert));
    }

    public void EnqueueReaderException(Exception exception, Action<ExecutedCommand>? assert = null)
    {
        _plans.Enqueue(new CommandPlan(CommandKind.Reader, () => throw exception, assert));
    }

    // --- DbConnection overrides ----------------------------------------------

    [AllowNull]
    public override string ConnectionString { get; set; } = string.Empty;

    public override string Database => "Fake";
    public override string DataSource => "Fake";
    public override string ServerVersion => "1.0";
    public override ConnectionState State => ConnectionState.Open;

    public override void ChangeDatabase(string databaseName) { }
    public override void Close() { }
    public override void Open() { }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        BeginTransactionCount++;
        LastTransaction = new FakeDbTransaction(this, isolationLevel);
        return LastTransaction;
    }

    protected override DbCommand CreateDbCommand() => new FakeDbCommand(this);

    // --- execution dispatch --------------------------------------------------

    internal object? Execute(CommandKind kind, FakeDbCommand command)
    {
        _plans.Should().NotBeEmpty("every repository call in these tests should have a planned DB response");
        var plan = _plans.Dequeue();
        plan.Kind.Should().Be(kind);

        var executed = new ExecutedCommand(
            command.CommandText,
            command.GetParameters(),
            HasTransaction: command.CurrentTransaction is not null,
            CommandTimeout: command.CommandTimeout);
        ExecutedCommands.Add(executed);
        plan.Assert?.Invoke(executed);
        return plan.ResultFactory();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// FakeDbCommand
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class FakeDbCommand : DbCommand
{
    private readonly FakeDbConnection _connection;
    private readonly FakeDbParameterCollection _parameters = new();

    public FakeDbCommand(FakeDbConnection connection)
    {
        _connection = connection;
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

    internal DbTransaction? CurrentTransaction => DbTransaction;

    public override void Cancel() { }

    public override int ExecuteNonQuery() =>
        (int)(_connection.Execute(CommandKind.NonQuery, this) ?? 0);

    public override object? ExecuteScalar() =>
        _connection.Execute(CommandKind.Scalar, this);

    public override void Prepare() { }

    protected override DbParameter CreateDbParameter() => new FakeDbParameter();

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
        (DbDataReader)(_connection.Execute(CommandKind.Reader, this)
            ?? throw new InvalidOperationException("Reader result was null."));

    internal Dictionary<string, object?> GetParameters() =>
        _parameters
            .Cast<FakeDbParameter>()
            .ToDictionary(
                parameter => parameter.ParameterName ?? string.Empty,
                parameter => parameter.Value,
                StringComparer.Ordinal);
}

// ─────────────────────────────────────────────────────────────────────────────
// FakeDbTransaction
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class FakeDbTransaction : DbTransaction
{
    private readonly FakeDbConnection _connection;

    public FakeDbTransaction(FakeDbConnection connection, IsolationLevel isolationLevel)
    {
        _connection = connection;
        IsolationLevel = isolationLevel;
    }

    public override IsolationLevel IsolationLevel { get; }

    protected override DbConnection DbConnection => _connection;

    public int CommitCount { get; private set; }
    public int RollbackCount { get; private set; }

    public override void Commit()
    {
        CommitCount++;
    }

    public override void Rollback()
    {
        RollbackCount++;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// FakeDbParameterCollection
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class FakeDbParameterCollection : DbParameterCollection
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

// ─────────────────────────────────────────────────────────────────────────────
// FakeDbParameter
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class FakeDbParameter : DbParameter
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

// ─────────────────────────────────────────────────────────────────────────────
// Supporting records & enum
// ─────────────────────────────────────────────────────────────────────────────

internal sealed record CommandPlan(
    CommandKind Kind,
    Func<object?> ResultFactory,
    Action<ExecutedCommand>? Assert);

/// <summary>
/// Captures everything about a command that was executed against the fake connection.
/// </summary>
internal sealed record ExecutedCommand(
    string CommandText,
    IReadOnlyDictionary<string, object?> Parameters,
    bool HasTransaction = false,
    int CommandTimeout = 30);

internal enum CommandKind
{
    Scalar,
    NonQuery,
    Reader
}

// ─────────────────────────────────────────────────────────────────────────────
// TestableSqlConnectionFactory
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Implements <see cref="ISqlConnectionFactory"/> backed by a
/// <see cref="FakeDbConnection"/> so repository tests can exercise
/// the full Dapper / ADO.NET code path without a real database.
/// </summary>
[SuppressMessage("Design", "CA1812",
    Justification = "Instantiated by test classes that are added incrementally.")]
internal sealed class TestableSqlConnectionFactory : ISqlConnectionFactory
{
    /// <summary>
    /// The underlying fake connection that all factory methods return.
    /// Enqueue plans on this instance before calling the system under test.
    /// </summary>
    public FakeDbConnection FakeConnection { get; }

    public TestableSqlConnectionFactory(FakeDbConnection? connection = null)
    {
        FakeConnection = connection ?? new FakeDbConnection();
    }

    // ── connection creation ──────────────────────────────────────────────

    public IDbConnection CreateConnection() => FakeConnection;

    public Task<IDbConnection> CreateConnectionAsync() =>
        Task.FromResult<IDbConnection>(FakeConnection);

    public Task<IDbConnection> CreateOpenConnectionAsync() =>
        Task.FromResult<IDbConnection>(FakeConnection);

    // ── command creation (delegates to the underlying connection) ────────

    public IDbCommand CreateCommand(IDbConnection connection) =>
        connection.CreateCommand();

    public IDbCommand CreateCommand(IDbConnection connection, IDbTransaction? transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        return command;
    }

    public IDbCommand CreateCommand(IDbConnection connection, string commandText)
    {
        var command = connection.CreateCommand();
        command.CommandText = commandText;
        return command;
    }

    public IDbCommand CreateCommand(
        IDbConnection connection,
        IDbTransaction? transaction,
        string? commandText,
        CommandType commandType,
        int? commandTimeout)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        command.CommandType = commandType;
        if (commandTimeout.HasValue)
        {
            command.CommandTimeout = commandTimeout.Value;
        }

        return command;
    }

    // ── parameter creation ───────────────────────────────────────────────

    public IDataParameter CreateParameter(string parameterName, object? value) =>
        new FakeDbParameter { ParameterName = parameterName, Value = value ?? DBNull.Value };

    public IDataParameter CreateParameter(string parameterName, object? value, DbType dbType) =>
        new FakeDbParameter
        {
            ParameterName = parameterName,
            Value = value ?? DBNull.Value,
            DbType = dbType
        };

    public IDataParameter CreateParameter(
        string parameterName,
        object? value,
        DbType dbType,
        ParameterDirection direction) =>
        new FakeDbParameter
        {
            ParameterName = parameterName,
            Value = value ?? DBNull.Value,
            DbType = dbType,
            Direction = direction
        };
}

// ─────────────────────────────────────────────────────────────────────────────
// TestHelpers
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Static helper methods for common test setup patterns shared across
/// Sql/Repositories unit tests.
/// </summary>
internal static class TestHelpers
{
    /// <summary>
    /// Returns a null logger of the specified category. Equivalent to
    /// <c>NullLogger&lt;T&gt;.Instance</c> but discoverable from the shared
    /// test infrastructure.
    /// </summary>
    public static ILogger<T> CreateNullLogger<T>() => NullLogger<T>.Instance;

    /// <summary>
    /// Wraps a plain options value in an <see cref="IOptions{TOptions}"/>
    /// suitable for passing to constructors.
    /// </summary>
    public static IOptions<T> OptionsFor<T>(T value) where T : class =>
        Microsoft.Extensions.Options.Options.Create(value);

    /// <summary>
    /// Builds a <see cref="DbDataReader"/> over the supplied rows. Column
    /// names are inferred as the union of all row keys (order-preserving,
    /// first-seen). Pass no rows to get an empty reader with a single
    /// "Id" column (use <see cref="CreateReaderWithSchema"/> if a specific
    /// empty schema is required).
    /// </summary>
    public static DbDataReader CreateReader(params IReadOnlyDictionary<string, object?>[] rows)
    {
        if (rows.Length == 0)
        {
            return CreateReaderWithSchema("Id");
        }

        var columnNames = rows.SelectMany(static row => row.Keys).Distinct(StringComparer.Ordinal).ToArray();
        return CreateReaderCore(columnNames, rows);
    }

    /// <summary>
    /// Builds an empty <see cref="DbDataReader"/> exposing the given column
    /// schema and zero rows. Useful for "no rows returned" test scenarios
    /// where the repository still reads the reader's schema.
    /// </summary>
    public static DbDataReader CreateReaderWithSchema(params string[] columnNames) =>
        CreateReaderCore(columnNames, Array.Empty<IReadOnlyDictionary<string, object?>>());

    /// <summary>
    /// Builds a multi-result-set <see cref="DbDataReader"/> suitable for repositories
    /// that use Dapper's <c>QueryMultipleAsync</c> (e.g. a page of rows followed by a
    /// scalar total count). Each element of <paramref name="resultSets"/> becomes one
    /// result set, consumed in order via <see cref="DbDataReader.NextResult"/>.
    /// </summary>
    public static DbDataReader CreateMultiResultReader(
        params IReadOnlyDictionary<string, object?>[][] resultSets)
    {
        var tables = resultSets.Select(BuildResultSetTable).ToArray();
        return new DataTableReader(tables);
    }

    /// <summary>
    /// Builds a single-row, single-column result set (e.g. a scalar <c>COUNT(1)</c>)
    /// for use as one of the result sets passed to <see cref="CreateMultiResultReader"/>.
    /// </summary>
    public static IReadOnlyDictionary<string, object?>[] ScalarResultSet(object? value) =>
        [new Dictionary<string, object?>(StringComparer.Ordinal) { ["Value"] = value }];

    private static DataTable BuildResultSetTable(IReadOnlyDictionary<string, object?>[] rows)
    {
        string[] columnNames = rows.Length == 0
            ? ["Value"]
            : rows.SelectMany(static row => row.Keys).Distinct(StringComparer.Ordinal).ToArray();

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

    [SuppressMessage("Reliability", "CA2000",
        Justification = "The DataTable backs the returned DataTableReader; disposing the reader " +
            "(owned by the caller) releases the underlying table's resources.")]
    private static DbDataReader CreateReaderCore(
        IReadOnlyList<string> columnNames,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
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

        return table.CreateDataReader();
    }
}
