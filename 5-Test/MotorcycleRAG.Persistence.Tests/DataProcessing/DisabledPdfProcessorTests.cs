using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
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
    public async Task ProcessAsync_FileNameContainsControlCharacters_LogsOneEscapedPhysicalLine()
    {
        const string attackerFileName = "manual\\name\r\nforged\tentry.pdf";
        const string expectedEscapedFileName = "manual\\\\name\\r\\nforged\\tentry.pdf";
        var logger = new CapturingLogger<DisabledPdfProcessor>();
        var input = CreateValidPdfDocument();
        input.FileName = attackerFileName;
        var sut = CreateSut(logger);

        var act = () => sut.ProcessAsync(input);

        await act.Should().ThrowAsync<InvalidOperationException>();
        var message = logger.Messages.Should().ContainSingle().Which;
        message.Should().Contain(expectedEscapedFileName)
            .And.NotContain("\r")
            .And.NotContain("\n")
            .And.NotContain("\t");
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NoopScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));

        private sealed class NoopScope : IDisposable
        {
            public static readonly NoopScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
