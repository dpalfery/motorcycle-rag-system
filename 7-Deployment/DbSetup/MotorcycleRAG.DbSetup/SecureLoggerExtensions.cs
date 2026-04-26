using Microsoft.Extensions.Logging;

namespace MotorcycleRAG.DbSetup;

public static class SecureLoggerExtensions
{
    public static ILoggingBuilder AddSecureLogging(this ILoggingBuilder builder)
    {
        return builder;
    }

    public static ILogger<T> CreateSecureLogger<T>(this ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        return new SanitizingLogger<T>(loggerFactory.CreateLogger<T>());
    }
}
