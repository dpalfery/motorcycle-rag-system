using Microsoft.Extensions.Logging;

namespace MotorcycleRAG.DbSetup;

public class SecureLogger
{
    private readonly ILogger _logger;

    public SecureLogger(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void RegisterSecret(string secret)
    {
        SensitiveLogRedactor.RegisterSecret(secret);
    }

    public void RegisterEnvironmentSecret(string envVarName)
    {
        SensitiveLogRedactor.RegisterEnvironmentSecret(envVarName);
    }

    public void ClearSecrets()
    {
        SensitiveLogRedactor.ClearSecrets();
    }

    public void LogInformation(string message, params object[] args)
    {
        var sanitizedMessage = SensitiveLogRedactor.SanitizeMessage(message);
        var sanitizedArgs = SensitiveLogRedactor.SanitizeArgs(args);

        _logger.LogInformation("{Message}", FormatSanitizedLog(sanitizedMessage, sanitizedArgs));
    }

    public void LogWarning(string message, params object[] args)
    {
        var sanitizedMessage = SensitiveLogRedactor.SanitizeMessage(message);
        var sanitizedArgs = SensitiveLogRedactor.SanitizeArgs(args);

        _logger.LogWarning("{Message}", FormatSanitizedLog(sanitizedMessage, sanitizedArgs));
    }

    public void LogError(string message, params object[] args)
    {
        var sanitizedMessage = SensitiveLogRedactor.SanitizeMessage(message);
        var sanitizedArgs = SensitiveLogRedactor.SanitizeArgs(args);

        _logger.LogError("{Message}", FormatSanitizedLog(sanitizedMessage, sanitizedArgs));
    }

    public void LogError(Exception exception, string message, params object[] args)
    {
        var sanitizedMessage = SensitiveLogRedactor.SanitizeMessage(message);
        var sanitizedArgs = SensitiveLogRedactor.SanitizeArgs(args);

        _logger.LogError("{Message} ExceptionType={ExceptionType}", sanitizedMessage, exception.GetType().Name);
    }

    public void LogDebug(string message, params object[] args)
    {
        var sanitizedMessage = SensitiveLogRedactor.SanitizeMessage(message);
        var sanitizedArgs = SensitiveLogRedactor.SanitizeArgs(args);

        _logger.LogDebug("{Message}", FormatSanitizedLog(sanitizedMessage, sanitizedArgs));
    }

    public void LogConnectionString(string connectionString, LogLevel level = LogLevel.Debug)
    {
        var sanitizedConnectionString = SensitiveLogRedactor.MaskConnectionStringSecrets(connectionString);

        switch (level)
        {
            case LogLevel.Debug:
                _logger.LogDebug("Connection string: {ConnectionString}", sanitizedConnectionString);
                break;
            case LogLevel.Information:
                _logger.LogInformation("Connection string: {ConnectionString}", sanitizedConnectionString);
                break;
            case LogLevel.Warning:
                _logger.LogWarning("Connection string: {ConnectionString}", sanitizedConnectionString);
                break;
            case LogLevel.Error:
                _logger.LogError("Connection string: {ConnectionString}", sanitizedConnectionString);
                break;
        }
    }

    private static string FormatSanitizedLog(string message, object[] args)
    {
        if (args.Length == 0)
        {
            return message;
        }

        return $"{message} | Args: {string.Join(", ", args)}";
    }
}
