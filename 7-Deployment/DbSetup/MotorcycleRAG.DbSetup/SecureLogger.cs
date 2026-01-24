using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

#pragma warning disable CS8603 // Possible null reference return - MaskConnectionStringSecrets is designed to never return null

namespace MotorcycleRAG.DbSetup;

public class SecureLogger
{
    private readonly ILogger _logger;
    private readonly HashSet<string> _secrets;

    public SecureLogger(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _secrets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public void RegisterSecret(string secret)
    {
        if (!string.IsNullOrEmpty(secret))
        {
            _secrets.Add(secret);
        }
    }

    public void RegisterEnvironmentSecret(string envVarName)
    {
        var secret = Environment.GetEnvironmentVariable(envVarName);
        if (!string.IsNullOrEmpty(secret))
        {
            _secrets.Add(secret);
        }
    }

    public void ClearSecrets()
    {
        _secrets.Clear();
    }

    public void LogInformation(string message, params object[] args)
    {
        var sanitizedMessage = SanitizeMessage(message);
        var sanitizedArgs = SanitizeArgs(args);

        _logger.LogInformation(sanitizedMessage, sanitizedArgs);
    }

    public void LogWarning(string message, params object[] args)
    {
        var sanitizedMessage = SanitizeMessage(message);
        var sanitizedArgs = SanitizeArgs(args);

        _logger.LogWarning(sanitizedMessage, sanitizedArgs);
    }

    public void LogError(string message, params object[] args)
    {
        var sanitizedMessage = SanitizeMessage(message);
        var sanitizedArgs = SanitizeArgs(args);

        _logger.LogError(sanitizedMessage, sanitizedArgs);
    }

    public void LogError(Exception exception, string message, params object[] args)
    {
        var sanitizedMessage = SanitizeMessage(message);
        var sanitizedArgs = SanitizeArgs(args);

        _logger.LogError(exception, sanitizedMessage, sanitizedArgs);
    }

    public void LogDebug(string message, params object[] args)
    {
        var sanitizedMessage = SanitizeMessage(message);
        var sanitizedArgs = SanitizeArgs(args);

        _logger.LogDebug(sanitizedMessage, sanitizedArgs);
    }

    public void LogConnectionString(string connectionString, LogLevel level = LogLevel.Debug)
    {
#pragma warning disable CS8603 // Possible null reference return
        var sanitizedConnectionString = MaskConnectionStringSecrets(connectionString);
#pragma warning restore CS8603 // Possible null reference return

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

    private string SanitizeMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return message;
        }

        var sanitized = message;

        // Mask any registered secrets in the message
        foreach (var secret in _secrets)
        {
            sanitized = ReplaceString(sanitized, secret, "REDACTED", StringComparison.OrdinalIgnoreCase);
        }

        // Mask common password patterns
        sanitized = Regex.Replace(sanitized, @"password['""\\s]*=[\\s]*['\""][^'\""]* ['\""]", "password=\"REDACTED\"", RegexOptions.IgnoreCase);
        sanitized = Regex.Replace(sanitized, @"pwd['""\\s]*=[\\s]*['\""][^'\""]* ['\""]", "pwd=\"REDACTED\"", RegexOptions.IgnoreCase);

        // Mask connection string password patterns
        sanitized = Regex.Replace(sanitized, @"password[=:][\\s]*[^\\s;]+", "password=REDACTED", RegexOptions.IgnoreCase);

        return sanitized;
    }

    private object[] SanitizeArgs(object[] args)
    {
        if (args == null || args.Length == 0)
        {
            return args ?? Array.Empty<object>();
        }

        var sanitizedArgs = new object[args.Length];

        for (int i = 0; i < args.Length; i++)
        {
            sanitizedArgs[i] = SanitizeArg(args[i]) ?? new object();
        }

        return sanitizedArgs;
    }

    private object? SanitizeArg(object? arg)
    {
        if (arg == null)
        {
            return arg;
        }

        var stringArg = arg.ToString();
        if (string.IsNullOrEmpty(stringArg))
        {
            return arg;
        }

        // Check if the argument matches any registered secret
        foreach (var secret in _secrets)
        {
            if (string.Equals(stringArg, secret, StringComparison.OrdinalIgnoreCase))
            {
                return "REDACTED";
            }
        }

        // Check for connection string patterns
        if (stringArg.Contains("password=", StringComparison.OrdinalIgnoreCase) ||
            stringArg.Contains("pwd=", StringComparison.OrdinalIgnoreCase))
        {
#pragma warning disable CS8603 // Possible null reference return
            return MaskConnectionStringSecrets(stringArg);
#pragma warning restore CS8603 // Possible null reference return
        }

        return arg;
    }

    private string MaskConnectionStringSecrets(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
        {
            return string.Empty;
        }

        var masked = connectionString;

        // Mask password in connection string
        masked = Regex.Replace(masked, @"password=[^;]+", "password=REDACTED", RegexOptions.IgnoreCase);

        // Mask pwd in connection string
        masked = Regex.Replace(masked, @"pwd=[^;]+", "pwd=REDACTED", RegexOptions.IgnoreCase);

        // Mask any registered secrets
        foreach (var secret in _secrets)
        {
            masked = ReplaceString(masked, secret, "REDACTED", StringComparison.OrdinalIgnoreCase);
        }

        return masked;
    }

    private static string ReplaceString(string input, string oldValue, string newValue, StringComparison comparison)
    {
        if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(oldValue))
        {
            return input;
        }

        var result = input;
        int index;

        while ((index = result.IndexOf(oldValue, comparison)) >= 0)
        {
            result = result.Substring(0, index) + newValue + result.Substring(index + oldValue.Length);
        }

        return result;
    }
}

public static class SecureLoggerExtensions
{
    public static ILoggingBuilder AddSecureLogging(this ILoggingBuilder builder)
    {
        return builder;
    }
}
