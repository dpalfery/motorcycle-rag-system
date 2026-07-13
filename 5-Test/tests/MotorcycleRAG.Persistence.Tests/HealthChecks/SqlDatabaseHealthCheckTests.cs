using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MotorcycleRAG.Persistence.HealthChecks;
using MotorcycleRAG.Persistence.Sql;

namespace MotorcycleRAG.Persistence.Tests.HealthChecks;

public class SqlDatabaseHealthCheckTests
{
    private static HealthCheckContext DefaultContext => new()
    {
        Registration = new HealthCheckRegistration("test", _ => null!, null, null)
    };

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenConnectionFactoryIsNull()
    {
        // Act
        var act = () => new SqlDatabaseHealthCheck(
            null!,
            TestHelpers.CreateNullLogger<SqlDatabaseHealthCheck>());

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("connectionFactory");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        // Act
        var act = () => new SqlDatabaseHealthCheck(
            Mock.Of<ISqlConnectionFactory>(),
            null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenConnectionIsNotSqlConnection_ReturnsUnhealthy()
    {
        // Arrange
        var mockFactory = new Mock<ISqlConnectionFactory>();
        mockFactory.Setup(f => f.CreateConnectionAsync())
            .ReturnsAsync(Mock.Of<IDbConnection>());

        var sut = new SqlDatabaseHealthCheck(
            mockFactory.Object,
            TestHelpers.CreateNullLogger<SqlDatabaseHealthCheck>());

        // Act
        var result = await sut.CheckHealthAsync(DefaultContext);

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("Failed to create SQL connection");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenFactoryThrowsException_ReturnsUnhealthy()
    {
        // Arrange
        var mockFactory = new Mock<ISqlConnectionFactory>();
        mockFactory.Setup(f => f.CreateConnectionAsync())
            .ThrowsAsync(new InvalidOperationException("Cannot create connection"));

        var sut = new SqlDatabaseHealthCheck(
            mockFactory.Object,
            TestHelpers.CreateNullLogger<SqlDatabaseHealthCheck>());

        // Act
        var result = await sut.CheckHealthAsync(DefaultContext);

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("Cannot create connection");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenOperationIsCanceled_ReturnsUnhealthy()
    {
        // Arrange
        var mockFactory = new Mock<ISqlConnectionFactory>();
        mockFactory.Setup(f => f.CreateConnectionAsync())
            .ThrowsAsync(new OperationCanceledException());

        var sut = new SqlDatabaseHealthCheck(
            mockFactory.Object,
            TestHelpers.CreateNullLogger<SqlDatabaseHealthCheck>());

        // Act
        var result = await sut.CheckHealthAsync(DefaultContext);

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("timed out");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenConnectionCannotOpen_ReturnsUnhealthy()
    {
        // Arrange
        // Use a fake DbConnection that throws on OpenAsync to avoid needing
        // a real SQL Server instance or a slow connection timeout.
        var mockFactory = new Mock<ISqlConnectionFactory>();
        mockFactory.Setup(f => f.CreateConnectionAsync())
            .ReturnsAsync(new ThrowingDbConnection());

        var sut = new SqlDatabaseHealthCheck(
            mockFactory.Object,
            TestHelpers.CreateNullLogger<SqlDatabaseHealthCheck>());

        // Act
        var result = await sut.CheckHealthAsync(DefaultContext);

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenSqlConnectionThrowsOnOpen_ReturnsUnhealthy()
    {
        // Arrange
        // new SqlConnection() without a connection string will throw on OpenAsync.
        // This exercises the path where `as SqlConnection` cast succeeds (line 32-33)
        // and OpenAsync is actually called (line 39), then caught by the general
        // catch block (line 61).
        var mockFactory = new Mock<ISqlConnectionFactory>();
        mockFactory.Setup(f => f.CreateConnectionAsync())
            .ReturnsAsync(new Microsoft.Data.SqlClient.SqlConnection());

        var sut = new SqlDatabaseHealthCheck(
            mockFactory.Object,
            TestHelpers.CreateNullLogger<SqlDatabaseHealthCheck>());

        // Act
        var result = await sut.CheckHealthAsync(DefaultContext);

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("SQL Database health check failed");
    }

    /// <summary>
    /// A fake DbConnection that throws on OpenAsync to simulate a connection
    /// that cannot be opened, without requiring a real SQL Server instance.
    /// </summary>
    private sealed class ThrowingDbConnection : DbConnection
    {
        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "fake";
        public override string DataSource => "fake";
        public override string ServerVersion => "1.0";
        public override ConnectionState State => ConnectionState.Closed;

        public override void Open() => throw new InvalidOperationException("Cannot open connection");
        public override Task OpenAsync(CancellationToken cancellationToken)
            => Task.FromException(new InvalidOperationException("Cannot open connection"));
        public override void Close() { }
        public override void ChangeDatabase(string databaseName) { }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
            => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand()
            => throw new NotSupportedException();
    }
}
