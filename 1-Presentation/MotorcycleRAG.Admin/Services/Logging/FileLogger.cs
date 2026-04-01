using Microsoft.Extensions.Logging;

namespace MotorcycleRAG.Admin.Services.Logging;

internal sealed class FileLogger : ILogger
{
    private readonly string _categoryName;
    private readonly FileLoggerProvider _provider;

    public FileLogger(string categoryName, FileLoggerProvider provider)
    {
        _categoryName = categoryName ?? throw new ArgumentNullException(nameof(categoryName));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= _provider.Options.MinimumLevel;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var message = formatter(state, exception);
        var exceptionText = exception != null ? $"\n{exception}" : string.Empty;
        var entry = $"{timestamp} [{logLevel}] {_categoryName}: {message}{exceptionText}";

        _provider.WriteEntry(entry);
    }
}
