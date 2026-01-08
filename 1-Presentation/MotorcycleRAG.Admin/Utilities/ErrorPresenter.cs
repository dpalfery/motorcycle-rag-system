using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace MotorcycleRAG.Admin.Utilities;

/// <summary>
/// Utility class for presenting errors to users
/// </summary>
internal static class ErrorPresenter
{
    private static readonly Regex FilePathRegex = new(@"([A-Za-z]:)?\\?(?:[^\\/]+\\)*[^\\/]+\.[a-zA-Z0-9]+", RegexOptions.Compiled);
    private static readonly Regex UrlRegex = new(@"https?://[^\s]+", RegexOptions.Compiled);
    private static readonly Regex IpAddressRegex = new(@"\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}", RegexOptions.Compiled);

    /// <summary>
    /// Sanitizes error messages to remove sensitive information before displaying to users
    /// Redacts file paths, URLs, IP addresses and limits message length
    /// </summary>
    internal static string SanitizeErrorMessage(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
            return "An unexpected error occurred. Please try again.";

        // Redact file paths (Windows and Unix styles)
        var sanitized = FilePathRegex.Replace(errorMessage, "[file path]");

        // Redact URLs
        sanitized = UrlRegex.Replace(sanitized, "[url]");

        // Redact IP addresses
        sanitized = IpAddressRegex.Replace(sanitized, "[ip address]");

        // Limit length to prevent excessively long messages
        if (sanitized.Length > 200)
            sanitized = string.Concat(sanitized.AsSpan(0, 197), "...");

        return sanitized;
    }

    /// <summary>
    /// Displays an error alert to the user
    /// </summary>
    internal static Task ShowErrorAsync(string title, string message)
    {
        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var window = Application.Current?.Windows?.FirstOrDefault();
            if (window?.Page != null)
            {
                await window.Page.DisplayAlertAsync(title, message, "OK");
            }
        });
    }

    /// <summary>
    /// Displays an exception to the user with optional details
    /// </summary>
    internal static Task ShowExceptionAsync(string title, Exception exception, bool showDetails = false)
    {
        var message = showDetails 
            ? $"{exception.Message}\n\nDetails: {exception}"
            : exception.Message;

        return ShowErrorAsync(title, message);
    }

    /// <summary>
    /// Displays a warning alert to the user
    /// </summary>
    internal static Task ShowWarningAsync(string title, string message)
    {
        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var window = Application.Current?.Windows?.FirstOrDefault();
            if (window?.Page != null)
            {
                await window.Page.DisplayAlertAsync(title, message, "OK");
            }
        });
    }

    /// <summary>
    /// Displays a success message to the user
    /// </summary>
    internal static Task ShowSuccessAsync(string title, string message)
    {
        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var window = Application.Current?.Windows?.FirstOrDefault();
            if (window?.Page != null)
            {
                await window.Page.DisplayAlertAsync(title, message, "OK");
            }
        });
    }

    /// <summary>
    /// Displays a confirmation dialog and returns user's choice
    /// </summary>
    internal static Task<bool> ShowConfirmAsync(string title, string message, string accept = "Yes", string cancel = "No")
    {
        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var window = Application.Current?.Windows?.FirstOrDefault();
            if (window?.Page != null)
            {
                return await window.Page.DisplayAlertAsync(title, message, accept, cancel);
            }
            return false;
        });
    }

    /// <summary>
    /// Logs error and optionally displays to user
    /// </summary>
    internal static async Task LogAndShowErrorAsync(string context, Exception exception, ILogger? logger, bool showToUser)
    {
        // Log using ILogger if available, otherwise no-op
        logger?.LogError(exception, "[ERROR] {Context}", context);

        // Show to user if requested
        if (showToUser)
        {
            await ShowExceptionAsync("Error", exception);
        }
    }

    /// <summary>
    /// Logs error and displays to user
    /// </summary>
    internal static Task LogAndShowErrorAsync(string context, Exception exception, ILogger? logger)
    {
        return LogAndShowErrorAsync(context, exception, logger, showToUser: true);
    }

    /// <summary>
    /// Logs error without displaying to user
    /// </summary>
    internal static Task LogAndShowErrorAsync(string context, Exception exception, bool showToUser)
    {
        return LogAndShowErrorAsync(context, exception, logger: null, showToUser);
    }

    /// <summary>
    /// Logs error without displaying to user
    /// </summary>
    internal static Task LogAndShowErrorAsync(string context, Exception exception)
    {
        return LogAndShowErrorAsync(context, exception, logger: null, showToUser: true);
    }
}
