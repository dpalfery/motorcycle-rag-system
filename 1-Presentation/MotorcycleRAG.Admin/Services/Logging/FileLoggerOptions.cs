using Microsoft.Extensions.Logging;

namespace MotorcycleRAG.Admin.Services.Logging;

internal sealed class FileLoggerOptions
{
    public string LogDirectory { get; init; } = string.Empty;
    public LogLevel MinimumLevel { get; init; } = LogLevel.Information;
}
