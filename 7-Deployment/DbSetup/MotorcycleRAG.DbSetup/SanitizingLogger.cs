using Microsoft.Extensions.Logging;

namespace MotorcycleRAG.DbSetup;

internal sealed class SanitizingLogger<T> : ILogger<T>
{
    private readonly ILogger<T> _innerLogger;

    public SanitizingLogger(ILogger<T> innerLogger)
    {
        _innerLogger = innerLogger ?? throw new ArgumentNullException(nameof(innerLogger));
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return _innerLogger.BeginScope(SensitiveLogRedactor.SanitizeArg(state) ?? string.Empty);
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return _innerLogger.IsEnabled(logLevel);
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        if (exception != null)
        {
            message = $"{message} ExceptionType={exception.GetType().Name}";
        }

        var sanitizedMessage = SensitiveLogRedactor.SanitizeMessage(message);
        _innerLogger.Log(logLevel, eventId, "{Message}", sanitizedMessage);
    }
}
