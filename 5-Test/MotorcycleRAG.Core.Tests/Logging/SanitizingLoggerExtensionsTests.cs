using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Core.Logging;

namespace MotorcycleRAG.Core.Tests.Logging;

public class SanitizingLoggerExtensionsTests
{
    [Fact]
    public void AddSanitizingLogger_WhenCalled_ReturnsSameLoggingBuilder()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act / Assert
        services.AddLogging(builder =>
        {
            var result = builder.AddSanitizingLogger();
            result.Should().BeSameAs(builder);
        });
    }

    [Fact]
    public void AddSanitizingLogger_WithSpyProvider_EscapesNewlinesInStructuredValues()
    {
        // Arrange
        var sink = new SpySink();
        var spyProvider = new SpyLoggerProvider(sink);
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.Services.AddSingleton<ILoggerProvider>(spyProvider);
            builder.AddSanitizingLogger();
        });

        var sp = services.BuildServiceProvider();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Test");

        // Act
        logger.LogInformation("User input: {Input}", "hello\nworld");

        // Assert
        var entry = sink.Entries.Should().ContainSingle().Subject;
        entry.Properties.Should().ContainKey("Input");
        var inputValue = entry.Properties["Input"] as string;
        inputValue.Should().Be("hello\\nworld");
    }

    [Fact]
    public void AddSanitizingLogger_EndToEnd_InnerProviderReceivesSanitizedOutput()
    {
        // Arrange
        var sink = new SpySink();
        var spyProvider = new SpyLoggerProvider(sink);
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.Services.AddSingleton<ILoggerProvider>(spyProvider);
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddSanitizingLogger();
        });

        var sp = services.BuildServiceProvider();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("E2E");

        // Act
        logger.LogWarning("Value={Value}", "line1\r\nline2\tindented");

        // Assert
        var entry = sink.Entries.Should().ContainSingle().Subject;
        entry.Properties.Should().ContainKey("Value");
        var value = entry.Properties["Value"] as string;
        value.Should().Be("line1\\r\\nline2\\tindented");
    }

    [Fact]
    public void AddSanitizingLogger_WithStructuredLogging_SanitizesMultipleProperties()
    {
        // Arrange
        var sink = new SpySink();
        var spyProvider = new SpyLoggerProvider(sink);
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.Services.AddSingleton<ILoggerProvider>(spyProvider);
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddSanitizingLogger();
        });

        var sp = services.BuildServiceProvider();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("MultiProp");

        // Act
        logger.LogError(
            "Processing {FileName} from {Source}",
            "doc\nname.pdf",
            "user\rinjected");

        // Assert
        var entry = sink.Entries.Should().ContainSingle().Subject;
        entry.Properties.Count.Should().Be(2);
        (entry.Properties["FileName"] as string).Should().Be("doc\\nname.pdf");
        (entry.Properties["Source"] as string).Should().Be("user\\rinjected");
    }

    [Fact]
    public void AddSanitizingLogger_WithCleanInput_PreservesValuesUnchanged()
    {
        // Arrange
        var sink = new SpySink();
        var spyProvider = new SpyLoggerProvider(sink);
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.Services.AddSingleton<ILoggerProvider>(spyProvider);
            builder.AddSanitizingLogger();
        });

        var sp = services.BuildServiceProvider();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Clean");

        // Act
        logger.LogInformation("Motorcycle {Model} year {Year}", "Fireblade", 2025);

        // Assert
        var entry = sink.Entries.Should().ContainSingle().Subject;
        (entry.Properties["Model"] as string).Should().Be("Fireblade");
        (entry.Properties["Year"] as string).Should().Be("2025");
    }

    /// <summary>
    /// Accumulates captured log entries across all loggers created by a
    /// <see cref="SpyLoggerProvider"/>.
    /// </summary>
    private sealed class SpySink
    {
        public List<CapturedEntry> Entries { get; } = [];
    }

    /// <summary>
    /// A <see cref="ILoggerProvider"/> that creates <see cref="SpyLogger"/> instances
    /// routing captured entries into a shared <see cref="SpySink"/>.
    /// </summary>
    private sealed class SpyLoggerProvider : ILoggerProvider
    {
        private readonly SpySink _sink;

        public SpyLoggerProvider(SpySink sink) => _sink = sink;

        public ILogger CreateLogger(string categoryName) =>
            new SpyLogger(_sink, categoryName);

        public void Dispose() { }
    }

    /// <summary>
    /// A test-double <see cref="ILogger"/> that records structured log state into a
    /// <see cref="SpySink"/> so tests can assert the values received by the inner
    /// provider after sanitization.
    /// </summary>
    private sealed class SpyLogger : ILogger
    {
        private readonly SpySink _sink;
        private readonly string _category;

        public SpyLogger(SpySink sink, string category)
        {
            _sink = sink;
            _category = category;
        }

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

            _sink.Entries.Add(new CapturedEntry(
                logLevel,
                formatter(state, exception),
                properties));
        }
    }

    /// <summary>
    /// A single captured log call exposing structured properties and the formatted
    /// message as observed by the inner logger.
    /// </summary>
    private sealed record CapturedEntry(
        LogLevel LogLevel,
        string FormattedMessage,
        IReadOnlyDictionary<string, object?> Properties);
}
