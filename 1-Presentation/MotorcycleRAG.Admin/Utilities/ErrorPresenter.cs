namespace MotorcycleRAG.Admin.Utilities;

/// <summary>
/// Utility class for presenting errors to users
/// </summary>
public static class ErrorPresenter
{
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
