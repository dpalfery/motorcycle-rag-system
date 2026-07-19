using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Utilities;
using MotorcycleRAG.Persistence.DataProcessing;

namespace MotorcycleRAG.Persistence.Tests.DataProcessing;

public class DisabledPdfProcessorTests
{
    private static DisabledPdfProcessor CreateSut(ILogger<DisabledPdfProcessor>? logger = null)
        => new(logger ?? TestHelpers.CreateNullLogger<DisabledPdfProcessor>());

    private static PDFDocument CreateValidPdfDocument()
        => new()
        {
            FileName = "test.pdf",
            Content = new MemoryStream("dummy content"u8.ToArray()),
            Make = "Honda",
            Model = "CBR600RR",
            Year = "2024"
        };

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        var act = () => new DisabledPdfProcessor(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_ShouldSucceed_WithValidLogger()
    {
        var act = () => CreateSut();
        act.Should().NotThrow();
    }

    [Fact]
    public async Task ProcessAsync_ShouldThrowArgumentNullException_WhenInputIsNull()
    {
        var sut = CreateSut();
        var act = () => sut.ProcessAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("input");
    }

    [Fact]
    public async Task ProcessAsync_ShouldThrowInvalidOperationException_WithExpectedMessage()
    {
        var sut = CreateSut();
        var input = CreateValidPdfDocument();
        var act = () => sut.ProcessAsync(input);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .Where(ex => ex.Message.Contains("Legacy PDF processing is disabled"));
    }

    [Fact]
    public async Task ProcessAsync_ShouldThrowInvalidOperationException_MentioningLocalPythonProcessor()
    {
        var sut = CreateSut();
        var input = CreateValidPdfDocument();
        var act = () => sut.ProcessAsync(input);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .Where(ex => ex.Message.Contains("local Python processor"));
    }

    [Fact]
    public void Implements_IDataProcessorOfPDFDocument()
    {
        var sut = CreateSut();
        sut.Should().BeAssignableTo<IDataProcessor<PDFDocument>>();
    }

    [Fact]
    public async Task ProcessAsync_FileNameContainsControlCharacters_LogsSanitizedFileName()
    {
        // The processor now sanitizes FileName inline via LogSanitizer.Sanitize before logging
        // (CWE-117 log-forging defense-in-depth alongside the runtime SanitizingLoggerProvider),
        // so the spy CapturingLogger observes the escaped value, not the raw one.
        const string attackerFileName = "manual\\name\r\nforged\tentry.pdf";
        var logger = new CapturingLogger<DisabledPdfProcessor>();
        var input = CreateValidPdfDocument();
        input.FileName = attackerFileName;
        var sut = CreateSut(logger);

        var act = () => sut.ProcessAsync(input);

        await act.Should().ThrowAsync<InvalidOperationException>();
        var entry = logger.Entries.Should().ContainSingle().Subject;
        var fileNameArg = entry.Properties.Should().ContainSingle(pair => pair.Key == "FileName")
            .Which.Value;
        fileNameArg.Should().Be(LogSanitizer.Sanitize(attackerFileName));
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<Dictionary<string, object?>> Scopes { get; } = [];

        public List<CapturedLogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            if (state is System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string, object?>> pairs)
            {
                Scopes.Add(pairs.ToDictionary(pair => pair.Key, pair => pair.Value));
            }

            return NoopScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state as System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string, object?>>
                ?? [];
            Entries.Add(new CapturedLogEntry(
                logLevel,
                exception,
                formatter(state, exception),
                properties.ToDictionary(pair => pair.Key, pair => pair.Value)));
        }

        private sealed class NoopScope : IDisposable
        {
            public static readonly NoopScope Instance = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed record CapturedLogEntry(
        LogLevel Level,
        Exception? Exception,
        string Message,
        System.Collections.Generic.IReadOnlyDictionary<string, object?> Properties);
}
