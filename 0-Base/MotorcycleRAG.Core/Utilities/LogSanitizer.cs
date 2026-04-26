namespace MotorcycleRAG.Core.Utilities;

/// <summary>
/// Provides a single canonical method for sanitizing user-controlled values before they are
/// written to structured log arguments, preventing log-injection attacks.
///
/// Log injection (OWASP ASVS V7.1 / CWE-117) is the insertion of control characters—primarily
/// newline (\n / \r) and tab (\t)—into logged strings to forge fake log entries or corrupt
/// log parsers. This helper:
///   1. Replaces \n, \r, \t, and null bytes with a space.
///   2. Truncates the value to <paramref name="maxLength"/> characters to prevent log bloat
///      and limit incidental data exposure.
///
/// Usage: wrap every user-supplied string argument passed to ILogger methods.
/// </summary>
public static class LogSanitizer
{
    /// <summary>
    /// Sanitizes a user-supplied value for safe inclusion as a structured log argument.
    /// Returns <see cref="string.Empty"/> for null input.
    /// </summary>
    /// <param name="value">The raw value to sanitize.</param>
    /// <param name="maxLength">
    /// Maximum number of characters to retain. Defaults to 200, which is sufficient for
    /// identifiers and short descriptors without truncating useful context.
    /// Use a smaller value (e.g. 48) when logging URLs or free-form text to limit log size.
    /// </param>
    /// <returns>A sanitized, truncated copy of the input, or <see cref="string.Empty"/> if null.</returns>
    public static string Sanitize(string? value, int maxLength = 200)
    {
        if (value is null)
            return string.Empty;

        // Replace control characters that enable log injection
        var sanitized = value
            .Replace('\n', ' ')
            .Replace('\r', ' ')
            .Replace('\t', ' ')
            .Replace('\0', ' ');

        return sanitized.Length > maxLength
            ? sanitized[..maxLength]
            : sanitized;
    }

    /// <summary>
    /// Sanitizes any value by converting it to a string before applying standard log sanitization.
    /// </summary>
    /// <param name="value">The raw value to sanitize.</param>
    /// <param name="maxLength">Maximum number of characters to retain.</param>
    /// <returns>A sanitized, truncated copy of the input, or <see cref="string.Empty"/> if null.</returns>
    public static string Sanitize(object? value, int maxLength = 200)
    {
        return Sanitize(value?.ToString(), maxLength);
    }
}
