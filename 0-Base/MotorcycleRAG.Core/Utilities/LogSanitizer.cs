using System.Globalization;
using System.Text;

namespace MotorcycleRAG.Core.Utilities;

/// <summary>
/// Provides a single canonical method for sanitizing user-controlled values before they are
/// written to structured log arguments, preventing log-injection attacks.
///
/// Log injection (OWASP ASVS V7.1 / CWE-117) is the insertion of control characters—primarily
/// newline (\n / \r) and tab (\t)—into logged strings to forge fake log entries or corrupt
/// log parsers. This helper:
///   1. Escapes backslashes before escaping controls, keeping values reversible.
///   2. Emits visible escapes for CR, LF, tab, null, and every remaining C0/C1 control
///      character so raw controls cannot forge log entries or corrupt log parsers.
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
    /// Optional maximum number of characters to retain. When omitted, the complete encoded value
    /// is preserved.
    /// </param>
    /// <returns>A sanitized copy of the input, or <see cref="string.Empty"/> if null.</returns>
    public static string Sanitize(string? value, int? maxLength = null)
    {
        if (value is null)
        {
            return string.Empty;
        }

        var sanitized = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            switch (character)
            {
                case '\\':
                    sanitized.Append("\\\\");
                    break;
                case '\r':
                    sanitized.Append("\\r");
                    break;
                case '\n':
                    sanitized.Append("\\n");
                    break;
                case '\t':
                    sanitized.Append("\\t");
                    break;
                case '\0':
                    sanitized.Append("\\0");
                    break;
                default:
                    if (character <= '\u001F' || (character >= '\u007F' && character <= '\u009F'))
                    {
                        sanitized.Append("\\u");
                        sanitized.Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sanitized.Append(character);
                    }

                    break;
            }
        }

        return maxLength is { } length && sanitized.Length > length
            ? sanitized.ToString(0, length)
            : sanitized.ToString();
    }

    /// <summary>
    /// Sanitizes any value by converting it to a string before applying standard log sanitization.
    /// </summary>
    /// <param name="value">The raw value to sanitize.</param>
    /// <param name="maxLength">Optional maximum number of characters to retain.</param>
    /// <returns>A sanitized copy of the input, or <see cref="string.Empty"/> if null.</returns>
    public static string Sanitize(object? value, int? maxLength = null)
    {
        return Sanitize(value?.ToString(), maxLength);
    }
}
