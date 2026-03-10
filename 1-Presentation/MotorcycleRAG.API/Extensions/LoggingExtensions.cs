namespace MotorcycleRAG.API.Extensions;

/// <summary>
/// Extension methods for <see cref="ILoggingBuilder"/>.
/// </summary>
internal static class LoggingExtensions
{
    /// <summary>
    /// Configures structured logging for the application.
    /// </summary>
    public static ILoggingBuilder AddStructuredLogging(this ILoggingBuilder logging, IWebHostEnvironment environment)
    {
        logging.ClearProviders();
        logging.AddConsole();
        logging.AddDebug();

        if (environment.IsProduction())
        {
            logging.AddJsonConsole();
        }

        return logging;
    }
}
