using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace MotorcycleRAG.DbSetup;

internal static class SensitiveLogRedactor
{
    private const string Redacted = "REDACTED";

    private static readonly ConcurrentDictionary<string, byte> Secrets = new(StringComparer.OrdinalIgnoreCase);

    public static void RegisterSecret(string? secret)
    {
        if (!string.IsNullOrWhiteSpace(secret))
        {
            Secrets.TryAdd(secret, 0);
        }
    }

    public static void RegisterEnvironmentSecret(string envVarName)
    {
        RegisterSecret(Environment.GetEnvironmentVariable(envVarName));
    }

    public static void ClearSecrets()
    {
        Secrets.Clear();
    }

    public static string SanitizeMessage(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return string.Empty;
        }

        var sanitized = message;

        foreach (var secret in Secrets.Keys)
        {
            sanitized = ReplaceString(sanitized, secret, Redacted, StringComparison.OrdinalIgnoreCase);
        }

        sanitized = MaskConnectionStringSecrets(sanitized);
        sanitized = Regex.Replace(sanitized, @"(?i)(password|pwd|secret|token|key)\s*[:=]\s*['""]?[^;,\s'""]+", "$1=REDACTED");

        return sanitized;
    }

    public static object[] SanitizeArgs(object[]? args)
    {
        if (args == null || args.Length == 0)
        {
            return Array.Empty<object>();
        }

        var sanitizedArgs = new object[args.Length];

        for (var i = 0; i < args.Length; i++)
        {
            sanitizedArgs[i] = SanitizeArg(args[i]) ?? string.Empty;
        }

        return sanitizedArgs;
    }

    public static object? SanitizeArg(object? arg)
    {
        if (arg == null)
        {
            return null;
        }

        var stringArg = arg.ToString();
        if (string.IsNullOrEmpty(stringArg))
        {
            return arg;
        }

        foreach (var secret in Secrets.Keys)
        {
            if (string.Equals(stringArg, secret, StringComparison.OrdinalIgnoreCase))
            {
                return Redacted;
            }
        }

        var sanitized = SanitizeMessage(stringArg);
        return sanitized == stringArg ? arg : sanitized;
    }

    public static string MaskConnectionStringSecrets(string? connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
        {
            return string.Empty;
        }

        var masked = Regex.Replace(connectionString, @"(?i)(password|pwd)\s*=\s*[^;]+", "$1=REDACTED");

        foreach (var secret in Secrets.Keys)
        {
            masked = ReplaceString(masked, secret, Redacted, StringComparison.OrdinalIgnoreCase);
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
            result = string.Concat(result.AsSpan(0, index), newValue, result.AsSpan(index + oldValue.Length));
        }

        return result;
    }
}
