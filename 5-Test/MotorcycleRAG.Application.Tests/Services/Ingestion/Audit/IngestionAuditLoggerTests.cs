using Microsoft.Extensions.Logging;
using MotorcycleRAG.Application.Services.Ingestion.Audit;

namespace MotorcycleRAG.UnitTests.Services.Ingestion.Audit;

public sealed class IngestionAuditLoggerTests
{
    [Fact]
    public async Task LogAsync_NewlineAndOverlongValues_LogsSanitizedAndTruncatedStructuredFields()
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
        entry.Properties["Success"].Should().Be(true);
        AssertSanitized(entry, eventName, "Event");
        AssertSanitized(entry, uploadId, "UploadId");
        AssertSanitized(entry, userId, "UserId");
        AssertNoRawNewlineData(entry);
    }

    [Fact]
    public async Task LogErrorAsync_NewlineAndOverlongValues_LogsSanitizedAndTruncatedStructuredFields()
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
        AssertSanitized(entry, eventName, "Event");
        AssertSanitized(entry, uploadId, "UploadId");
        AssertSanitized(entry, userId, "UserId");
        AssertSanitized(entry, errorCode, "ErrorCode");
        AssertNoRawNewlineData(entry);
    }

    private static string CreateUnsafeValue(string prefix) =>
        $"{prefix}\n{new string('x', 205)}";

    private static void AssertSanitized(CapturedLogEntry entry, string rawValue, string propertyName)
    {
        var sanitizedValue = entry.Properties[propertyName].Should().BeOfType<string>().Subject;

        sanitizedValue.Should().HaveLength(200);
        sanitizedValue.Should().NotBe(rawValue);
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
                    if (!string.Equals(pair.Key, "{OriginalFormat}", StringComparison.Ordinal))
                    {
                        properties[pair.Key] = pair.Value;
                    }
                }
            }

            _entries.Add(new CapturedLogEntry(
                logLevel,
                formatter(state, exception),
                properties));
        }
    }
}
