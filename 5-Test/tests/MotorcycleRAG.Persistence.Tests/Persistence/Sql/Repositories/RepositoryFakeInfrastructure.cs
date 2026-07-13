using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace MotorcycleRAG.UnitTests.Persistence.Sql.Repositories;

/// <summary>
/// Shared fake ADO.NET infrastructure for SQL repository unit tests.
/// Provides a FakeDbConnection with an enqueue/plan pattern so tests can
/// pre-program responses and assert on executed commands.
/// </summary>

// ─────────────────────────────────────────────────────────────────────────────
// FakeDbConnection
// ─────────────────────────────────────────────────────────────────────────────

[SuppressMessage("Design", "CA1812",
    Justification = "Instantiated by test classes.")]
internal sealed class FakeDbConnection : DbConnection
{
    private readonly Queue<CommandPlan> _plans = new();

    public List<ExecutedCommand> ExecutedCommands { get; } = [];

    public int BeginTransactionCount { get; private set; }

    public FakeDbTransaction? LastTransaction { get; private set; }

    // --- enqueue helpers -------------------------------------------------

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

    // --- DbConnection overrides ------------------------------------------

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

    // --- execution dispatch ----------------------------------------------

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
// Test reader helpers
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Static helper methods for creating <see cref="DbDataReader"/> instances
/// from in-memory data for use with <see cref="FakeDbConnection.EnqueueReader"/>.
/// </summary>
internal static class RepositoryTestReader
{
    /// <summary>
    /// Builds a <see cref="DbDataReader"/> over the supplied rows using object-typed
    /// columns. Suitable for most repository tests; use <see cref="CreateTypedReader"/>
    /// when the repository maps Dapper enums from NVARCHAR columns.
    /// </summary>
    public static DbDataReader CreateReader(params IReadOnlyDictionary<string, object?>[] rows)
    {
        if (rows.Length == 0)
        {
            return CreateReaderWithSchema("Id");
        }

        var columnNames = rows.SelectMany(static row => row.Keys).Distinct(StringComparer.Ordinal).ToArray();
        return CreateReaderCore(columnNames, Array.Empty<Type>(), rows);
    }

    /// <summary>
    /// Builds an empty <see cref="DbDataReader"/> with the given column names.
    /// </summary>
    public static DbDataReader CreateReaderWithSchema(params string[] columnNames) =>
        CreateReaderCore(columnNames, Array.Empty<Type>(), []);

    /// <summary>
    /// Builds a properly-typed <see cref="DbDataReader"/> where each column's
    /// <see cref="DbDataReader.GetFieldType"/> returns the actual CLR type of the
    /// first non-null value. Required for repositories where Dapper maps enums from
    /// NVARCHAR columns (Dapper only takes its enum-aware "parse from string" code path
    /// when the reader reports the column's field type as <see cref="string"/>).
    /// </summary>
    public static DbDataReader CreateTypedReader(params IReadOnlyDictionary<string, object?>[] rows)
    {
        if (rows.Length == 0)
        {
            return CreateReaderWithSchema("Id");
        }

        var columnNames = rows.SelectMany(static row => row.Keys).Distinct(StringComparer.Ordinal).ToArray();
        var columnTypes = columnNames
            .Select(name => rows
                .Select(row => row.TryGetValue(name, out var value) ? value : null)
                .FirstOrDefault(static value => value is not null)?
                .GetType() ?? typeof(object))
            .ToArray();

        return CreateReaderCore(columnNames, columnTypes, rows);
    }

    /// <summary>
    /// Builds a <see cref="DbDataReader"/> for joined projections (Dapper multi-mapping
    /// with splitOn). Unlike a <see cref="DataTable"/>-backed reader, this allows
    /// duplicate column names as a real SQL join projection would.
    /// </summary>
    public static DbDataReader CreateMultiMapReader(
        IReadOnlyList<string> columnNames,
        IReadOnlyList<Type> columnTypes,
        IReadOnlyList<object?[]> rows) =>
        new MultiMapDataReader(columnNames, columnTypes, rows);

    [SuppressMessage("Reliability", "CA2000",
        Justification = "The DataTable backs the returned DataTableReader; disposing the reader " +
            "(owned by the caller) releases the underlying table's resources.")]
    private static DbDataReader CreateReaderCore(
        IReadOnlyList<string> columnNames,
        IReadOnlyList<Type> columnTypes,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        var table = new DataTable();
        for (var i = 0; i < columnNames.Count; i++)
        {
            var columnType = i < columnTypes.Count ? columnTypes[i] : typeof(object);
            table.Columns.Add(columnNames[i], columnType);
        }

        foreach (var row in rows)
        {
            var dataRow = table.NewRow();
            for (var i = 0; i < columnNames.Count; i++)
            {
                dataRow[columnNames[i]] = row.TryGetValue(columnNames[i], out var value) ? value ?? DBNull.Value : DBNull.Value;
            }

            table.Rows.Add(dataRow);
        }

        return table.CreateDataReader();
    }

    /// <summary>
    /// Minimal forward-only <see cref="DbDataReader"/> that allows duplicate column names,
    /// needed for Dapper multi-mapping (splitOn) on joined projections.
    /// </summary>
    private sealed class MultiMapDataReader : DbDataReader
    {
        private readonly IReadOnlyList<string> _columnNames;
        private readonly IReadOnlyList<Type> _columnTypes;
        private readonly IReadOnlyList<object?[]> _rows;
        private int _rowIndex = -1;

        public MultiMapDataReader(IReadOnlyList<string> columnNames, IReadOnlyList<Type> columnTypes, IReadOnlyList<object?[]> rows)
        {
            _columnNames = columnNames;
            _columnTypes = columnTypes;
            _rows = rows;
        }

        public override int FieldCount => _columnNames.Count;
        public override int VisibleFieldCount => FieldCount;
        public override bool HasRows => _rows.Count > 0;
        public override int Depth => 0;
        public override bool IsClosed => false;
        public override int RecordsAffected => -1;

        public override object this[int ordinal] => GetValue(ordinal);
        public override object this[string name] => GetValue(GetOrdinal(name));

        public override bool Read()
        {
            _rowIndex++;
            return _rowIndex < _rows.Count;
        }

        public override Task<bool> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Read());

        public override bool NextResult() => false;

        public override Task<bool> NextResultAsync(CancellationToken cancellationToken) => Task.FromResult(false);

        public override string GetName(int ordinal) => _columnNames[ordinal];

        public override int GetOrdinal(string name)
        {
            for (var i = 0; i < _columnNames.Count; i++)
            {
                if (string.Equals(_columnNames[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            throw new ArgumentOutOfRangeException(name);
        }

        public override object GetValue(int ordinal) => _rows[_rowIndex][ordinal] ?? DBNull.Value;

        public override int GetValues(object[] values)
        {
            var row = _rows[_rowIndex];
            var count = Math.Min(values.Length, row.Length);
            for (var i = 0; i < count; i++)
            {
                values[i] = row[i] ?? DBNull.Value;
            }

            return count;
        }

        public override bool IsDBNull(int ordinal) => _rows[_rowIndex][ordinal] is null;

        public override Type GetFieldType(int ordinal) => _columnTypes[ordinal];

        public override string GetDataTypeName(int ordinal) => GetFieldType(ordinal).Name;

        public override bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);
        public override byte GetByte(int ordinal) => (byte)GetValue(ordinal);
        public override char GetChar(int ordinal) => (char)GetValue(ordinal);
        public override DateTime GetDateTime(int ordinal) => GetValue(ordinal) switch
        {
            DateTimeOffset offset => offset.UtcDateTime,
            var value => (DateTime)value
        };
        public override decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);
        public override double GetDouble(int ordinal) => (double)GetValue(ordinal);
        public override float GetFloat(int ordinal) => (float)GetValue(ordinal);
        public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);
        public override short GetInt16(int ordinal) => (short)GetValue(ordinal);
        public override int GetInt32(int ordinal) => (int)GetValue(ordinal);
        public override long GetInt64(int ordinal) => (long)GetValue(ordinal);
        public override string GetString(int ordinal) => (string)GetValue(ordinal);

        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) =>
            throw new NotSupportedException();

        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) =>
            throw new NotSupportedException();

        public override IEnumerator GetEnumerator() => _rows.GetEnumerator();
    }
}
