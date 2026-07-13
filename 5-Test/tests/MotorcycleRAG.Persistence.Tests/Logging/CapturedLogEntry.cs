using Microsoft.Extensions.Logging;

namespace MotorcycleRAG.UnitTests.Logging;

/// <summary>
/// A single captured log call from <see cref="SpyLogger{TCategory}"/>, exposing the structured
/// message properties (the named placeholders) as a dictionary so tests can assert on individual
/// log fields rather than the formatted message string.
/// </summary>
internal sealed record CapturedLogEntry(
    LogLevel LogLevel,
    EventId EventId,
    Exception? Exception,
    string FormattedMessage,
    IReadOnlyDictionary<string, object?> Properties);
