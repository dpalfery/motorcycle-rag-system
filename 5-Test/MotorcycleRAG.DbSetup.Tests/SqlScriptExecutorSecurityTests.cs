using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.DbSetup;

namespace MotorcycleRAG.DbSetup.Tests;

public sealed class SqlScriptExecutorSecurityTests
{
    [Fact]
    public void Constructor_WhenCatalogRootIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var factory = new SuccessfulConnectionFactory();

        // Act
        var act = () => new SqlScriptExecutor(
            NullLogger<SqlScriptExecutor>.Instance,
            factory,
            null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("catalogRoot");
    }

    [Fact]
    public async Task ExecuteScriptAsync_WhenSchemaIsARegularCatalogFile_ExecutesWithInjectedConnection()
    {
        // Arrange
        var testDirectory = CreateTemporaryDirectory();
        var catalogRoot = Path.Combine(testDirectory, "catalog");
        CreateCatalog(catalogRoot);
        var factory = new SuccessfulConnectionFactory();
        var sut = new SqlScriptExecutor(NullLogger<SqlScriptExecutor>.Instance, factory, catalogRoot);

        try
        {
            // Act
            var result = await sut.ExecuteScriptAsync("fake-connection", DbSetupScript.Schema);

            // Assert
            result.Should().BeTrue();
            factory.CreateCallCount.Should().Be(1);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteScriptAsync_WhenSchemaCatalogFileIsASymlinkToOutside_RejectsBeforeOpeningConnection()
    {
        // Arrange
        var testDirectory = CreateTemporaryDirectory();
        var catalogRoot = Path.Combine(testDirectory, "catalog");
        var outsideScriptPath = Path.Combine(testDirectory, "outside-schema.sql");
        CreateCatalog(catalogRoot);
        await File.WriteAllTextAsync(outsideScriptPath, "SELECT 'outside';");
        File.Delete(GetSchemaPath(catalogRoot));
        File.CreateSymbolicLink(GetSchemaPath(catalogRoot), outsideScriptPath);
        var factory = new SuccessfulConnectionFactory();
        var sut = new SqlScriptExecutor(NullLogger<SqlScriptExecutor>.Instance, factory, catalogRoot);

        try
        {
            // Act
            var result = await sut.ExecuteScriptAsync("fake-connection", DbSetupScript.Schema);

            // Assert
            result.Should().BeFalse();
            factory.CreateCallCount.Should().Be(0);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteScriptAsync_WhenSchemaCatalogDirectoryIsASymlinkToOutside_RejectsBeforeOpeningConnection()
    {
        // Arrange
        var testDirectory = CreateTemporaryDirectory();
        var catalogRoot = Path.Combine(testDirectory, "catalog");
        var outsideDirectory = Path.Combine(testDirectory, "outside-sql");
        CreateCatalog(catalogRoot);
        Directory.CreateDirectory(outsideDirectory);
        await File.WriteAllTextAsync(Path.Combine(outsideDirectory, "schema.sql"), "SELECT 'outside';");
        Directory.Delete(Path.GetDirectoryName(GetSchemaPath(catalogRoot))!, recursive: true);
        Directory.CreateSymbolicLink(Path.GetDirectoryName(GetSchemaPath(catalogRoot))!, outsideDirectory);
        var factory = new SuccessfulConnectionFactory();
        var sut = new SqlScriptExecutor(NullLogger<SqlScriptExecutor>.Instance, factory, catalogRoot);

        try
        {
            // Act
            var result = await sut.ExecuteScriptAsync("fake-connection", DbSetupScript.Schema);

            // Assert
            result.Should().BeFalse();
            factory.CreateCallCount.Should().Be(0);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    private static void CreateCatalog(string catalogRoot)
    {
        var schemaPath = GetSchemaPath(catalogRoot);
        var testDataPath = Path.Combine(catalogRoot, "7-Deployment", "DbSetup", "sql", "test-data.sql");

        Directory.CreateDirectory(Path.GetDirectoryName(schemaPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(testDataPath)!);
        File.WriteAllText(schemaPath, "SELECT 1;");
        File.WriteAllText(testDataPath, "SELECT 1;");
    }

    private static string GetSchemaPath(string catalogRoot) =>
        Path.Combine(catalogRoot, "4-Persistence", "MotorcycleRAG.Persistence", "Sql", "schema.sql");

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"motorcyclerag-dbsetup-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class SuccessfulConnectionFactory : IDbSetupConnectionFactory
    {
        public int CreateCallCount { get; private set; }

        public DbConnection Create(string connectionString)
        {
            CreateCallCount++;
            return new SuccessfulDbConnection();
        }
    }

    private sealed class SuccessfulDbConnection : DbConnection
    {
        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "Fake";
        public override string DataSource => "Fake";
        public override string ServerVersion => "1.0";
        public override ConnectionState State => ConnectionState.Open;

        public override void ChangeDatabase(string databaseName) { }
        public override void Close() { }
        public override void Open() { }
        public override Task OpenAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
            throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => new SuccessfulDbCommand();
    }

    private sealed class SuccessfulDbCommand : DbCommand
    {
        [AllowNull]
        public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection? DbConnection { get; set; }
        protected override DbParameterCollection DbParameterCollection => throw new NotSupportedException();
        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel() { }
        public override int ExecuteNonQuery() => 1;
        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken) => Task.FromResult(1);
        public override object? ExecuteScalar() => 1;
        public override void Prepare() { }

        protected override DbParameter CreateDbParameter() => throw new NotSupportedException();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
            throw new NotSupportedException();
    }
}
