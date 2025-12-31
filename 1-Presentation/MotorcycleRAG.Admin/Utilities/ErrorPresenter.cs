using System.Text.RegularExpressions;

namespace MotorcycleRAG.Admin.Utilities;

/// <summary>
/// Utility class for presenting errors to users
/// </summary>
public static class ErrorPresenter
{
    /// <summary>
    /// Sanitizes error messages to remove sensitive information before displaying to users
    /// Redacts file paths, URLs, IP addresses and limits message length
    /// </summary>
    public static string SanitizeErrorMessage(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
            return "An unexpected error occurred. Please try again.";

        // Redact file paths (Windows and Unix styles)
        var sanitized = Regex.Replace(
            errorMessage,
            @"([A-Za-z]:)?\\?(?:[^\\/]+\\)*[^\\/]+\.[a-zA-Z0-9]+",
            "[file path]",
            RegexOptions.Compiled);

        // Redact URLs
        sanitized = Regex.Replace(
            sanitized,
            @"https?://[^\s]+",
            "[url]",
            RegexOptions.Compiled);

        // Redact IP addresses
        sanitized = Regex.Replace(
            sanitized,
            @"\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}",
            "[ip address]",
            RegexOptions.Compiled);

        // Limit length to prevent excessively long messages
        if (sanitized.Length > 200)
            sanitized = sanitized.Substring(0, 197) + "...";

        return sanitized;
    }

    /// <summary>
    /// Displays an error alert to the user
    /// </summary>
    public static Task ShowErrorAsync(string title, string message)
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
    public static Task ShowExceptionAsync(string title, Exception exception, bool showDetails = false)
    {
        var message = showDetails 
            ? $"{exception.Message}\n\nDetails: {exception}"
            : exception.Message;

        return ShowErrorAsync(title, message);
    }

    /// <summary>
    /// Displays a warning alert to the user
    /// </summary>
    public static Task ShowWarningAsync(string title, string message)
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
    public static Task ShowSuccessAsync(string title, string message)
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
    public static Task<bool> ShowConfirmAsync(string title, string message, string accept = "Yes", string cancel = "No")
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
    /// Logs error to console and optionally displays to user
    /// </summary>
    public static async Task LogAndShowErrorAsync(string context, Exception exception, bool showToUser = true)
    {
        // Log to console
        Console.WriteLine($"[ERROR] {context}: {exception}");

        // Show to user if requested
        if (showToUser)
        {
            await ShowExceptionAsync("Error", exception);
        }
    }
}
