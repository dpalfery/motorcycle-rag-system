using Microsoft.Extensions.Logging;
using MotorcycleRAG.Persistence.Telemetry;
using MotorcycleRAG.UnitTests.Logging;

namespace MotorcycleRAG.UnitTests.Telemetry;

public sealed class IngestionTelemetryServiceTests
{
    [Fact]
    public void TrackIngestionStarted_SanitisesUserIdBeforeStructuredLogging()
    {
        // Arrange
        var logger = new SpyLogger<IngestionTelemetryService>();
        var sut = new IngestionTelemetryService(logger);
        var jobId = Guid.NewGuid();

        // Act
        sut.TrackIngestionStarted(jobId, "manual-pdf", "operator\nadmin\r\t1");

        // Assert
        logger.Entries.Should().ContainSingle();
        var entry = logger.Entries[0];
        entry.LogLevel.Should().Be(LogLevel.Information);
        entry.Properties["JobId"].Should().Be(jobId);
        entry.Properties["DocumentType"].Should().Be("manual-pdf");
        entry.Properties["UserId"].Should().Be("operator admin  1");
        entry.FormattedMessage.Should().NotContain("\n").And.NotContain("\r").And.NotContain("\t");
    }

    [Fact]
    public void TrackIngestionCompleted_LogsDurationAndProcessedItemCount()
    {
        // Arrange
        var logger = new SpyLogger<IngestionTelemetryService>();
        var sut = new IngestionTelemetryService(logger);
        var jobId = Guid.NewGuid();

        // Act
        sut.TrackIngestionCompleted(jobId, "csv", TimeSpan.FromMilliseconds(1250), 42);

        // Assert
        var entry = logger.Entries.Should().ContainSingle().Which;
        entry.LogLevel.Should().Be(LogLevel.Information);
        entry.Properties["JobId"].Should().Be(jobId);
        entry.Properties["DocumentType"].Should().Be("csv");
        entry.Properties["DurationMs"].Should().Be(1250d);
        entry.Properties["ItemsProcessed"].Should().Be(42);
    }

    [Fact]
    public void TrackIngestionFailed_LogsOriginalExceptionWithoutUserData()
    {
        // Arrange
        var logger = new SpyLogger<IngestionTelemetryService>();
        var sut = new IngestionTelemetryService(logger);
        var jobId = Guid.NewGuid();
        var failure = new InvalidOperationException("provider unavailable");

        // Act
        sut.TrackIngestionFailed(jobId, "manual-pdf", failure, TimeSpan.FromSeconds(2));

        // Assert
        var entry = logger.Entries.Should().ContainSingle().Which;
        entry.LogLevel.Should().Be(LogLevel.Error);
        entry.Exception.Should().BeSameAs(failure);
        entry.Properties["JobId"].Should().Be(jobId);
        entry.Properties["DocumentType"].Should().Be("manual-pdf");
        entry.Properties["DurationMs"].Should().Be(2000d);
        entry.FormattedMessage.Should().NotContain("\n").And.NotContain("\r");
    }

    [Fact]
    public void ConstructorOrStartWithNullRequiredDependency_ThrowsArgumentNullException()
    {
        // Arrange
        var sut = new IngestionTelemetryService(new SpyLogger<IngestionTelemetryService>());

        // Act
        var nullLogger = () => new IngestionTelemetryService(null!);
        var nullUser = () => sut.TrackIngestionStarted(Guid.NewGuid(), "manual-pdf", null!);

        // Assert
        nullLogger.Should().Throw<ArgumentNullException>();
        nullUser.Should().Throw<ArgumentNullException>();
    }
}
