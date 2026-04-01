using System.Text;
using Microsoft.Extensions.Logging;

namespace MotorcycleRAG.Admin.Services.Logging;

internal sealed class FileLoggerProvider : ILoggerProvider, IDisposable
{
    private readonly FileLoggerOptions _options;
    private readonly object _writerLock = new();
    private StreamWriter? _currentWriter;
    private DateTime _currentWriteDate = DateTime.MinValue;
    private bool _disposed;

    public string LogDirectory => _options.LogDirectory;
    internal FileLoggerOptions Options => _options;

    public FileLoggerProvider(FileLoggerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        try
        {
            if (!Directory.Exists(_options.LogDirectory))
            {
                Directory.CreateDirectory(_options.LogDirectory);
            }
        }
        catch
        {
            // Directory creation failed — provider will silently fail to write
        }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, this);

    internal void WriteEntry(string entry)
    {
        if (string.IsNullOrEmpty(_options.LogDirectory))
        {
            return;
        }

        lock (_writerLock)
        {
            try
            {
                var today = DateTime.Now.Date;

                // Roll to a new file if the date has changed
                if (today != _currentWriteDate)
                {
                    _currentWriter?.Flush();
                    _currentWriter?.Dispose();
                    _currentWriter = null;
                    _currentWriteDate = today;
                }

                // Open the writer for today if not already open
                if (_currentWriter == null)
                {
                    var filePath = Path.Combine(_options.LogDirectory, $"app-{today:yyyy-MM-dd}.log");
                    _currentWriter = new StreamWriter(filePath, append: true, encoding: Encoding.UTF8, bufferSize: 1024)
                    {
                        AutoFlush = true
                    };
                }

                _currentWriter.WriteLine(entry);
            }
            catch
            {
                // Silently ignore write failures
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_writerLock)
        {
            _currentWriter?.Flush();
            _currentWriter?.Dispose();
            _currentWriter = null;
        }

        _disposed = true;
    }
}
