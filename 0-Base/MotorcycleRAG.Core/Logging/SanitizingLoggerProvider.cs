using System.Collections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Core.Utilities;

namespace MotorcycleRAG.Core.Logging;

/// <summary>
/// An <see cref="ILoggerProvider"/> decorator that wraps every produced <see cref="ILogger"/>
/// in a <see cref="SanitizingLogger"/>, ensuring structured log state values are passed through
/// <see cref="LogSanitizer.Sanitize(object?)"/> before they reach any underlying logger sink.
/// </summary>
/// <remarks>
/// <para>
/// This provider exists to defeat Snyk CWE-117 (log forging) data-flow false negatives: Snyk's
/// taint analysis does not recognize the codebase's manual <c>LogSanitizer.Sanitize()</c> wraps,
/// so a central decorator that intercepts <em>all</em> log calls provides a single, verifiable
/// sanitization boundary. Hosts must call <see cref="SanitizingLoggerExtensions.AddSanitizingLogger"/>
/// <em>after</em> registering every other logger provider so this wrapper captures them all.
/// </para>
/// <para>
/// Exceptions are intentionally <em>not</em> sanitized: they are diagnostic artifacts whose
/// stack traces and message text must remain intact for triage, and the only user-controlled
/// surface (typically <c>ex.Message</c>) is rendered by sinks that already escape it.
/// </para>
/// </remarks>
public sealed class SanitizingLoggerProvider : ILoggerProvider
{
    private readonly ILoggerProvider _innerProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="SanitizingLoggerProvider"/> class.
    /// </summary>
    /// <param name="innerProvider">The real provider whose loggers should be wrapped.</param>
    public SanitizingLoggerProvider(ILoggerProvider innerProvider)
    {
        _innerProvider = innerProvider ?? throw new ArgumentNullException(nameof(innerProvider));
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName)
    {
        var innerLogger = _innerProvider.CreateLogger(categoryName);
        return new SanitizingLogger(innerLogger);
    }

    /// <inheritdoc />
    public void Dispose() => _innerProvider.Dispose();
}

/// <summary>
/// An <see cref="ILogger"/> decorator that sanitizes every structured log state value via
/// <see cref="LogSanitizer"/> before forwarding the call to the inner logger.
/// </summary>
public sealed class SanitizingLogger : ILogger
{
    private readonly ILogger _innerLogger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SanitizingLogger"/> class.
    /// </summary>
    /// <param name="innerLogger">The wrapped logger that receives the sanitized state.</param>
    public SanitizingLogger(ILogger innerLogger)
    {
        _innerLogger = innerLogger ?? throw new ArgumentNullException(nameof(innerLogger));
    }

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        if (state is IReadOnlyList<KeyValuePair<string, object?>> stateList)
        {
            var sanitized = new List<KeyValuePair<string, object?>>(stateList.Count);
            foreach (var pair in stateList)
            {
                if (pair.Key == "{OriginalFormat}")
                {
                    sanitized.Add(new(pair.Key, LogSanitizer.Sanitize(pair.Value?.ToString())));
                }
                else
                {
                    sanitized.Add(new(pair.Key, SanitizeValue(pair.Value)));
                }
            }

            var wrappedState = new SanitizedLogState<TState>(state, sanitized);
            return _innerLogger.BeginScope(wrappedState);
        }

        // Fallback for non-standard scope state: sanitize the string representation so the
        // inner sink never observes a raw control character carried in scope state.
        var sanitizedState = LogSanitizer.Sanitize(state.ToString());
        return _innerLogger.BeginScope(sanitizedState);
    }

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => _innerLogger.IsEnabled(logLevel);

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        if (!IsEnabled(logLevel) || state is null)
        {
            _innerLogger.Log(logLevel, eventId, state, exception, formatter);
            return;
        }

        if (state is IReadOnlyList<KeyValuePair<string, object?>> stateList)
        {
            var sanitized = new List<KeyValuePair<string, object?>>(stateList.Count);
            foreach (var pair in stateList)
            {
                if (pair.Key == "{OriginalFormat}")
                {
                    sanitized.Add(new(pair.Key, LogSanitizer.Sanitize(pair.Value?.ToString())));
                }
                else
                {
                    sanitized.Add(new(pair.Key, SanitizeValue(pair.Value)));
                }
            }

            var wrappedState = new SanitizedLogState<TState>(state, sanitized);
            _innerLogger.Log(
                logLevel,
                eventId,
                wrappedState,
                exception,
                (s, ex) => formatter(s.OriginalState, ex));
        }
        else
        {
            // Fallback for non-standard state: sanitize the formatted output so the inner sink
            // never observes a raw control character even when the caller bypassed structured
            // logging. Exceptions are passed through untouched (see class remarks).
            var formatted = formatter(state, exception);
            _innerLogger.Log(
                logLevel,
                eventId,
                LogSanitizer.Sanitize(formatted),
                exception,
                (s, ex) => s?.ToString() ?? string.Empty);
        }
    }

    private static string SanitizeValue(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        return LogSanitizer.Sanitize(value.ToString());
    }
}

/// <summary>
/// Wraps the original <typeparamref name="TState"/> together with the sanitized
/// <see cref="KeyValuePair{TKey, TValue}"/> list so the inner logger still observes an
/// <see cref="IReadOnlyList{T}"/> of structured state (preserving placeholder names for
/// App Insights customDimensions / Serilog properties) while the original state remains
/// reachable for the formatter via <see cref="OriginalState"/>.
/// </summary>
internal sealed class SanitizedLogState<TState> : IReadOnlyList<KeyValuePair<string, object?>>
{
    private readonly IReadOnlyList<KeyValuePair<string, object?>> _sanitized;

    public SanitizedLogState(
        TState originalState,
        IReadOnlyList<KeyValuePair<string, object?>> sanitized)
    {
        OriginalState = originalState;
        _sanitized = sanitized;
    }

    public TState OriginalState { get; }

    public int Count => _sanitized.Count;

    public KeyValuePair<string, object?> this[int index] => _sanitized[index];

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _sanitized.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Extension methods for registering <see cref="SanitizingLoggerProvider"/> on
/// <see cref="ILoggerFactory"/> builder pipelines.
/// </summary>
public static class SanitizingLoggerExtensions
{
    /// <summary>
    /// Wraps every <see cref="ILoggerProvider"/> currently registered with
    /// <paramref name="builder"/> in a <see cref="SanitizingLoggerProvider"/> so that all
    /// subsequent log calls are sanitized at the central logger boundary.
    /// </summary>
    /// <remarks>
    /// This MUST be called <em>after</em> all other <c>Add*</c> logging provider registrations
    /// (e.g. <c>AddConsole</c>, <c>AddDebug</c>, Application Insights, OpenTelemetry) so the
    /// existing registrations are captured. Any providers registered after this call will not
    /// be wrapped.
    /// </remarks>
    public static ILoggingBuilder AddSanitizingLogger(this ILoggingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var existingProviders = builder.Services
            .Where(d => d.ServiceType == typeof(ILoggerProvider))
            .ToList();

        foreach (var descriptor in existingProviders)
        {
            builder.Services.Remove(descriptor);
        }

        foreach (var descriptor in existingProviders)
        {
            if (descriptor.ImplementationInstance is ILoggerProvider instance)
            {
                builder.Services.AddSingleton<ILoggerProvider>(_ =>
                    new SanitizingLoggerProvider(instance));
            }
            else if (descriptor.ImplementationType is { } capturedType)
            {
                builder.Services.AddSingleton<ILoggerProvider>(sp =>
                    new SanitizingLoggerProvider(
                        (ILoggerProvider)ActivatorUtilities.CreateInstance(sp, capturedType)));
            }
            else if (descriptor.ImplementationFactory is { } capturedFactory)
            {
                builder.Services.AddSingleton<ILoggerProvider>(sp =>
                    new SanitizingLoggerProvider(
                        (ILoggerProvider)capturedFactory(sp)));
            }
        }

        return builder;
    }
}
