using Microsoft.Extensions.Logging;
using MotorcycleRAG.Application.Services.Ingestion.Audit;

namespace MotorcycleRAG.UnitTests.Services.Ingestion.Audit;

public sealed class IngestionAuditLoggerTests
{
    [Fact]
    public async Task LogAsync_NewlineAndOverlongValues_LogsReversibleFullStructuredFields()
    {
        // Arrange
        var logger = new CapturingLogger<IngestionAuditLogger>();
        var sut = new IngestionAuditLogger(logger);
        var eventName = CreateUnsafeValue("event");
        var uploadId = CreateUnsafeValue("upload");
        var userId = CreateUnsafeValue("user");

        // Act
        await sut.LogAsync(eventName, uploadId, userId, success: true);

        // Assert
        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.LogLevel.Should().Be(LogLevel.Information);
        entry.Properties.Should().ContainKeys("Event", "UploadId", "UserId", "Success", "{OriginalFormat}");
        entry.Properties["{OriginalFormat}"].Should().Be(
            "Ingestion audit: event={Event} uploadId={UploadId} userId={UserId} success={Success}");
        entry.Properties["Success"].Should().Be(true);
        AssertSanitized(entry, eventName, "Event");
        AssertSanitized(entry, uploadId, "UploadId");
        AssertSanitized(entry, userId, "UserId");
        AssertNoRawNewlineData(entry);
    }

    [Fact]
    public async Task LogErrorAsync_NewlineAndOverlongValues_LogsReversibleFullStructuredFields()
    {
        // Arrange
        var logger = new CapturingLogger<IngestionAuditLogger>();
        var sut = new IngestionAuditLogger(logger);
        var eventName = CreateUnsafeValue("event");
        var uploadId = CreateUnsafeValue("upload");
        var userId = CreateUnsafeValue("user");
        var errorCode = CreateUnsafeValue("error");

        // Act
        await sut.LogErrorAsync(eventName, uploadId, userId, errorCode);

        // Assert
        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.LogLevel.Should().Be(LogLevel.Warning);
        entry.Properties.Should().ContainKeys("Event", "UploadId", "UserId", "ErrorCode", "{OriginalFormat}");
        entry.Properties["{OriginalFormat}"].Should().Be(
            "Ingestion audit error: event={Event} uploadId={UploadId} userId={UserId} errorCode={ErrorCode}");
        AssertSanitized(entry, eventName, "Event");
        AssertSanitized(entry, uploadId, "UploadId");
        AssertSanitized(entry, userId, "UserId");
        AssertSanitized(entry, errorCode, "ErrorCode");
        AssertNoRawNewlineData(entry);
    }

    private static string CreateUnsafeValue(string prefix) =>
        $"{prefix}\\source\r\n{new string('x', 205)}\ttail";

    private static void AssertSanitized(CapturedLogEntry entry, string rawValue, string propertyName)
    {
        var sanitizedValue = entry.Properties[propertyName].Should().BeOfType<string>().Subject;

        sanitizedValue.Length.Should().BeGreaterThan(200);
        sanitizedValue.Should().NotBe(rawValue);
        sanitizedValue.Should().Contain("\\\\source\\r\\n");
        sanitizedValue.Should().EndWith($"{new string('x', 205)}\\ttail");
        sanitizedValue.Should().NotContain("\n");
        sanitizedValue.Should().NotContain("\r");
        sanitizedValue.Should().NotContain("\t");
        sanitizedValue.Should().NotContain("\0");
    }

    private static void AssertNoRawNewlineData(CapturedLogEntry entry)
    {
        entry.Message.Should().NotContain("\n");
        entry.Message.Should().NotContain("\r");
        entry.Message.Should().NotContain("\t");
        entry.Message.Should().NotContain("\0");
    }

    private sealed record CapturedLogEntry(
        LogLevel LogLevel,
        string Message,
        IReadOnlyDictionary<string, object?> Properties);

    private sealed class CapturingLogger<TCategory> : ILogger<TCategory>
    {
        private readonly List<CapturedLogEntry> _entries = [];

        public IReadOnlyList<CapturedLogEntry> Entries => _entries;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = new Dictionary<string, object?>(StringComparer.Ordinal);

            if (state is IReadOnlyList<KeyValuePair<string, object?>> stateList)
            {
                foreach (var pair in stateList)
                {
                    properties[pair.Key] = pair.Value;
                }
            }

            _entries.Add(new CapturedLogEntry(
                logLevel,
                formatter(state, exception),
                properties));
        }
    }
}
