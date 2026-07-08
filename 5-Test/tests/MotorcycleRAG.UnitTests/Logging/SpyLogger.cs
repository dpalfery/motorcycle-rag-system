using Microsoft.Extensions.Logging;

namespace MotorcycleRAG.UnitTests.Logging;

/// <summary>
/// A minimal <see cref="ILogger{TCategoryName}"/> test double that records every log call and
/// extracts the structured message state (<see cref="IReadOnlyList{T}"/> of key/value pairs) into
/// <see cref="CapturedLogEntry.Properties"/>, so tests can assert on individual structured fields
/// (e.g. <c>Succeeded</c>, <c>IndexName</c>, <c>DurationMs</c>) instead of formatted text.
/// </summary>
/// <remarks>
/// The framework-injected <c>{OriginalFormat}</c> pair is intentionally excluded from
/// <see cref="CapturedLogEntry.Properties"/> so only the caller-supplied placeholders remain.
/// </remarks>
internal sealed class SpyLogger<TCategory> : ILogger<TCategory>
{
    private readonly List<CapturedLogEntry> _entries = [];

    /// <summary>
    /// The captured log entries, in invocation order.
    /// </summary>
    public IReadOnlyList<CapturedLogEntry> Entries => _entries;

    /// <inheritdoc />
    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
        => new NullScope();

    private sealed class NullScope : IDisposable
    {
        public void Dispose() { }
    }

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
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
            eventId,
            exception,
            formatter(state, exception),
            properties));
    }
}
