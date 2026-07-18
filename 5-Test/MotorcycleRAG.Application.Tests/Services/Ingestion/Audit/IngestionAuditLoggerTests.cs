using Microsoft.Extensions.Logging;
using MotorcycleRAG.Application.Services.Ingestion.Audit;

namespace MotorcycleRAG.UnitTests.Services.Ingestion.Audit;

public sealed class IngestionAuditLoggerTests
{
    [Fact]
    public async Task LogAsync_LogsStructuredFieldsAtInformationLevel()
    {
        // Arrange
        var logger = new CapturingLogger<IngestionAuditLogger>();
        var sut = new IngestionAuditLogger(logger);
        const string eventName = "UploadReceived";
        const string uploadId = "upload-123";
        const string userId = "user-456";

        // Act
        await sut.LogAsync(eventName, uploadId, userId, success: true);

        // Assert
        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.LogLevel.Should().Be(LogLevel.Information);
        entry.Properties.Should().ContainKeys("Event", "UploadId", "UserId", "Success", "{OriginalFormat}");
        entry.Properties["{OriginalFormat}"].Should().Be(
            "Ingestion audit: event={Event} uploadId={UploadId} userId={UserId} success={Success}");
        entry.Properties["Event"].Should().Be(eventName);
        entry.Properties["UploadId"].Should().Be(uploadId);
        entry.Properties["UserId"].Should().Be(userId);
        entry.Properties["Success"].Should().Be(true);
    }

    [Fact]
    public async Task LogErrorAsync_LogsStructuredFieldsAtWarningLevel()
    {
        // Arrange
        var logger = new CapturingLogger<IngestionAuditLogger>();
        var sut = new IngestionAuditLogger(logger);
        const string eventName = "UploadValidationFailed";
        const string uploadId = "upload-789";
        const string userId = "user-012";
        const string errorCode = "FILE_TOO_LARGE";

        // Act
        await sut.LogErrorAsync(eventName, uploadId, userId, errorCode);

        // Assert
        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.LogLevel.Should().Be(LogLevel.Warning);
        entry.Properties.Should().ContainKeys("Event", "UploadId", "UserId", "ErrorCode", "{OriginalFormat}");
        entry.Properties["{OriginalFormat}"].Should().Be(
            "Ingestion audit error: event={Event} uploadId={UploadId} userId={UserId} errorCode={ErrorCode}");
        entry.Properties["Event"].Should().Be(eventName);
        entry.Properties["UploadId"].Should().Be(uploadId);
        entry.Properties["UserId"].Should().Be(userId);
        entry.Properties["ErrorCode"].Should().Be(errorCode);
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
