using FluentAssertions;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Core.Logging;

namespace MotorcycleRAG.Core.Tests.Logging;

public class SanitizingLoggerProviderTests
{
    #region Spy infrastructure

    /// <summary>
    /// Spy <see cref="ILogger"/> that captures everything passed to <see cref="Log{TState}"/>.
    /// </summary>
    private sealed class SpyLogger : ILogger
    {
        public LogLevel? LastLogLevel { get; private set; }
        public EventId? LastEventId { get; private set; }
        public Exception? LastException { get; private set; }
        public IReadOnlyList<KeyValuePair<string, object?>>? LastState { get; private set; }
        public string? LastFormattedMessage { get; private set; }
        public SpyDisposable? LastScope { get; private set; }
        public bool IsEnabledReturnValue { get; set; } = true;
        public int LogCallCount { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            LastScope = new SpyDisposable();
            return LastScope;
        }

        public bool IsEnabled(LogLevel logLevel) => IsEnabledReturnValue;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            LastLogLevel = logLevel;
            LastEventId = eventId;
            LastException = exception;
            LastFormattedMessage = formatter(state, exception);
            LogCallCount++;

            if (state is IReadOnlyList<KeyValuePair<string, object?>> stateList)
            {
                LastState = stateList;
            }
        }
    }

    private sealed class SpyDisposable : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private sealed class SpyLoggerProvider : ILoggerProvider
    {
        private readonly ILogger _logger;
        public bool DisposeCalled { get; private set; }

        public SpyLoggerProvider(ILogger logger) => _logger = logger;
        public ILogger CreateLogger(string categoryName) => _logger;
        public void Dispose() => DisposeCalled = true;
    }

    #endregion

    #region Helpers

    private static List<KeyValuePair<string, object?>> CreateState(params (string Key, object? Value)[] entries)
    {
        return entries.Select(e => new KeyValuePair<string, object?>(e.Key, e.Value)).ToList();
    }

    private static string GetStateValue(IReadOnlyList<KeyValuePair<string, object?>> state, string key)
    {
        return state.FirstOrDefault(p => p.Key == key).Value?.ToString() ?? string.Empty;
    }

    #endregion

    #region SanitizingLoggerProvider tests

    [Fact]
    public void CreateLogger_WhenCalled_ReturnsSanitizingLogger()
    {
        // Arrange
        var innerLogger = new SpyLogger();
        var innerProvider = new SpyLoggerProvider(innerLogger);
        var sut = new SanitizingLoggerProvider(innerProvider);

        // Act
        var logger = sut.CreateLogger("TestCategory");

        // Assert
        logger.Should().NotBeNull();

        // Log through it to verify sanitization reaches the inner logger
        var state = CreateState(("input", "hello\nworld"), ("{OriginalFormat}", "Test: {input}"));
        logger.Log(LogLevel.Information, default, state, null, (s, _) => "formatted");

        innerLogger.LastState.Should().NotBeNull();
        GetStateValue(innerLogger.LastState!, "input").Should().Be("hello\\nworld");
    }

    [Fact]
    public void Dispose_WhenCalled_DisposesInnerProvider()
    {
        // Arrange
        var innerLogger = new SpyLogger();
        var innerProvider = new SpyLoggerProvider(innerLogger);
        var sut = new SanitizingLoggerProvider(innerProvider);

        // Act
        sut.Dispose();

        // Assert
        innerProvider.DisposeCalled.Should().BeTrue();
    }

    #endregion

    #region Control character matrix — named escapes

    [Theory]
    [InlineData("\r", "\\r")]
    [InlineData("\n", "\\n")]
    [InlineData("\t", "\\t")]
    [InlineData("\0", "\\0")]
    [InlineData("\\", "\\\\")]
    public void Log_WhenStateValueContainsControlChar_DelegatesSanitizedValueToInner(string inputValue, string expectedSanitized)
    {
        // Arrange
        var spyLogger = new SpyLogger();
        var sut = new SanitizingLogger(spyLogger);
        var state = CreateState(("msg", inputValue), ("{OriginalFormat}", "Msg: {msg}"));

        // Act
        sut.Log(LogLevel.Information, default, state, null, (s, _) => "formatted");

        // Assert
        spyLogger.LastState.Should().NotBeNull();
        GetStateValue(spyLogger.LastState!, "msg").Should().Be(expectedSanitized);
    }

    [Fact]
    public void Log_WhenStateValueContainsMultipleAdjacentControlChars_EscapesEachIndividually()
    {
        // Arrange
        var spyLogger = new SpyLogger();
        var sut = new SanitizingLogger(spyLogger);
        // "\n\r" — newline then carriage return — each individually escaped for reversibility
        var state = CreateState(("msg", "\n\r"), ("{OriginalFormat}", "Msg: {msg}"));

        // Act
        sut.Log(LogLevel.Information, default, state, null, (s, _) => "formatted");

        // Assert
        spyLogger.LastState.Should().NotBeNull();
        GetStateValue(spyLogger.LastState!, "msg").Should().Be("\\n\\r");
    }

    #endregion

    #region Control character matrix — C0 and C1 ranges

    [Fact]
    public void Log_WhenStateValueContainsC0Characters_DelegatesUnicodeEscapedValuesToInner()
    {
        // Arrange
        var spyLogger = new SpyLogger();
        var sut = new SanitizingLogger(spyLogger);
        // 0x01 (SOH) and 0x1F (US) — C0 controls not covered by named escapes
        var input = $"before{(char)0x01}middle{(char)0x1F}after";
        var state = CreateState(("msg", input), ("{OriginalFormat}", "Msg: {msg}"));

        // Act
        sut.Log(LogLevel.Information, default, state, null, (s, _) => "formatted");

        // Assert
        spyLogger.LastState.Should().NotBeNull();
        GetStateValue(spyLogger.LastState!, "msg").Should().Be("before\\u0001middle\\u001Fafter");
    }

    [Fact]
    public void Log_WhenStateValueContainsC1Characters_DelegatesUnicodeEscapedValuesToInner()
    {
        // Arrange
        var spyLogger = new SpyLogger();
        var sut = new SanitizingLogger(spyLogger);
        // 0x85 (NEL) — a C1 control character
        var input = $"before{(char)0x85}after";
        var state = CreateState(("msg", input), ("{OriginalFormat}", "Msg: {msg}"));

        // Act
        sut.Log(LogLevel.Information, default, state, null, (s, _) => "formatted");

        // Assert
        spyLogger.LastState.Should().NotBeNull();
        GetStateValue(spyLogger.LastState!, "msg").Should().Be("before\\u0085after");
    }

    [Fact]
    public void Log_WhenStateValueContainsUnicodeNonControl_PreservesValue()
    {
        // Arrange
        var spyLogger = new SpyLogger();
        var sut = new SanitizingLogger(spyLogger);
        // U+2028 (LINE SEPARATOR) — outside C0 and C1 ranges, should be preserved as-is
        var input = $"hello{(char)0x2028}world";
        var state = CreateState(("msg", input), ("{OriginalFormat}", "Msg: {msg}"));

        // Act
        sut.Log(LogLevel.Information, default, state, null, (s, _) => "formatted");

        // Assert
        spyLogger.LastState.Should().NotBeNull();
        GetStateValue(spyLogger.LastState!, "msg").Should().Be(input);
    }

    #endregion

    #region Structured state sanitization

    [Fact]
    public void Log_WhenStateHasMixedTaint_SanitizesTaintedValuesAndPreservesCleanValuesAndPlaceholderNames()
    {
        // Arrange
        var spyLogger = new SpyLogger();
        var sut = new SanitizingLogger(spyLogger);
        var state = CreateState(
            ("tainted", "evil\r\ninjection"),
            ("clean", "safe"),
            ("{OriginalFormat}", "User said: {tainted} and {clean}"));

        // Act
        sut.Log(LogLevel.Information, default, state, null, (s, _) => "formatted");

        // Assert
        spyLogger.LastState.Should().NotBeNull();
        spyLogger.LastState!.Count.Should().Be(3);

        // Tainted value is sanitized
        GetStateValue(spyLogger.LastState, "tainted").Should().Be("evil\\r\\ninjection");
        // Clean value is preserved unchanged
        GetStateValue(spyLogger.LastState, "clean").Should().Be("safe");
        // Placeholder names are unchanged
        spyLogger.LastState.Should().Contain(p => p.Key == "tainted");
        spyLogger.LastState.Should().Contain(p => p.Key == "clean");
        spyLogger.LastState.Should().Contain(p => p.Key == "{OriginalFormat}");
    }

    [Fact]
    public void Log_WhenStateValueIsNull_ReturnsEmptyString()
    {
        // Arrange
        var spyLogger = new SpyLogger();
        var sut = new SanitizingLogger(spyLogger);
        var state = CreateState(("msg", null), ("{OriginalFormat}", "Msg: {msg}"));

        // Act
        sut.Log(LogLevel.Information, default, state, null, (s, _) => "formatted");

        // Assert
        spyLogger.LastState.Should().NotBeNull();
        GetStateValue(spyLogger.LastState!, "msg").Should().BeEmpty();
    }

    #endregion

    #region {OriginalFormat} template sanitization

    [Fact]
    public void Log_WhenOriginalFormatTemplateContainsControlChar_SanitizesTemplate()
    {
        // Arrange
        var spyLogger = new SpyLogger();
        var sut = new SanitizingLogger(spyLogger);
        var state = CreateState(
            ("msg", "hello"),
            ("{OriginalFormat}", "Template with\nnewline and \ttab"));

        // Act
        sut.Log(LogLevel.Information, default, state, null, (s, _) => "formatted");

        // Assert
        spyLogger.LastState.Should().NotBeNull();
        GetStateValue(spyLogger.LastState!, "{OriginalFormat}").Should().Be("Template with\\nnewline and \\ttab");
    }

    #endregion

    #region Exception passthrough

    [Fact]
    public void Log_WhenExceptionProvided_PassesExceptionThroughUnchanged()
    {
        // Arrange
        var spyLogger = new SpyLogger();
        var sut = new SanitizingLogger(spyLogger);
        var exception = new InvalidOperationException("Test exception with sensitive\ndata");
        var state = CreateState(("msg", "test"), ("{OriginalFormat}", "{msg}"));

        // Act
        sut.Log(LogLevel.Error, default, state, exception, (s, ex) => "formatted");

        // Assert
        // Exception object identity is preserved — not stringified, not sanitized
        spyLogger.LastException.Should().BeSameAs(exception);
        spyLogger.LastException!.Message.Should().Be("Test exception with sensitive\ndata");
    }

    [Fact]
    public void Log_WhenExceptionIsNull_PassesNullThroughToInnerLogger()
    {
        // Arrange
        var spyLogger = new SpyLogger();
        var sut = new SanitizingLogger(spyLogger);
        var state = CreateState(("msg", "test"), ("{OriginalFormat}", "{msg}"));

        // Act
        sut.Log(LogLevel.Information, default, state, null, (s, _) => "formatted");

        // Assert
        spyLogger.LastException.Should().BeNull();
    }

    #endregion

    #region Scope passthrough

    [Fact]
    public void BeginScope_WhenCalled_DelegatesToInnerLoggerAndReturnsSameDisposable()
    {
        // Arrange
        var spyLogger = new SpyLogger();
        var sut = new SanitizingLogger(spyLogger);

        // Act
        var scope = sut.BeginScope("test-scope");

        // Assert
        spyLogger.LastScope.Should().NotBeNull();
        scope.Should().BeSameAs(spyLogger.LastScope);
    }

    #endregion

    #region IsEnabled passthrough

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsEnabled_WhenCalled_DelegatesToInnerLogger(bool innerResult)
    {
        // Arrange
        var spyLogger = new SpyLogger { IsEnabledReturnValue = innerResult };
        var sut = new SanitizingLogger(spyLogger);

        // Act
        var result = sut.IsEnabled(LogLevel.Critical);

        // Assert
        result.Should().Be(innerResult);
    }

    #endregion

    #region Null and empty state

    [Fact]
    public void Log_WhenStateIsNull_DoesNotThrowAndForwardsToInnerLogger()
    {
        // Arrange
        var spyLogger = new SpyLogger();
        var sut = new SanitizingLogger(spyLogger);

        // Act
        var act = () => sut.Log(
            LogLevel.Information,
            default,
            (IReadOnlyList<KeyValuePair<string, object?>>)null!,
            null,
            (s, _) => "fallback");

        // Assert
        act.Should().NotThrow();
        spyLogger.LogCallCount.Should().Be(1);
        spyLogger.LastFormattedMessage.Should().Be("fallback");
    }

    [Fact]
    public void Log_WhenStateIsEmptyList_DoesNotThrowAndForwardsToInnerLogger()
    {
        // Arrange
        var spyLogger = new SpyLogger();
        var sut = new SanitizingLogger(spyLogger);
        var emptyState = new List<KeyValuePair<string, object?>>();

        // Act
        var act = () => sut.Log(LogLevel.Information, default, emptyState, null, (s, _) => "formatted");

        // Assert
        act.Should().NotThrow();
        spyLogger.LastState.Should().NotBeNull();
        spyLogger.LastState.Should().BeEmpty();
    }

    #endregion

    #region Fallback path (non-IReadOnlyList state)

    [Fact]
    public void Log_WhenStateIsNotReadOnlyList_SanitizesFormattedOutput()
    {
        // Arrange
        var spyLogger = new SpyLogger();
        var sut = new SanitizingLogger(spyLogger);
        // Use a custom type as state — not IReadOnlyList<KeyValuePair<string, object?>>
        // so the sanitizing logger takes the fallback path and sanitizes the formatted message
        var customState = new FallbackTestState { Value = "hello\nworld\t!" };

        // Act
        sut.Log(LogLevel.Information, default, customState, null, (s, _) => $"value: {s.Value}");

        // Assert
        spyLogger.LastFormattedMessage.Should().Be("value: hello\\nworld\\t!");
        // Fallback path passes a sanitized string, not structured state
        spyLogger.LastState.Should().BeNull();
    }

    private readonly struct FallbackTestState
    {
        public string Value { get; init; }
    }

    #endregion

    #region IsEnabled false — raw state passthrough

    [Fact]
    public void Log_WhenIsEnabledReturnsFalse_DelegatesRawStateDirectlyWithoutSanitization()
    {
        // Arrange
        var spyLogger = new SpyLogger { IsEnabledReturnValue = false };
        var sut = new SanitizingLogger(spyLogger);
        var state = CreateState(("msg", "\r\ntainted"), ("{OriginalFormat}", "{msg}"));

        // Act
        sut.Log(LogLevel.Debug, default, state, null, (s, _) => "formatted");

        // Assert
        // When IsEnabled returns false, the raw (unsanitized) state passes through directly
        spyLogger.LogCallCount.Should().Be(1);
        spyLogger.LastState.Should().NotBeNull();
        GetStateValue(spyLogger.LastState!, "msg").Should().Be("\r\ntainted");
    }

    #endregion

    #region Formatter exception handling

    [Fact]
    public void Log_WhenFormatterIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var spyLogger = new SpyLogger();
        var sut = new SanitizingLogger(spyLogger);

        // Act
        var act = () => sut.Log<string>(
            LogLevel.Information,
            default,
            "test",
            null,
            null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion
}
