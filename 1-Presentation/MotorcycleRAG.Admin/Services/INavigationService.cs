namespace MotorcycleRAG.Admin.Services;

/// <summary>
/// Abstraction for Shell-based navigation.
/// This interface enables testability of ViewModels by decoupling them from Shell.Current.
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// Navigates to the specified route with optional query parameters.
    /// </summary>
    /// <param name="route">The route to navigate to (e.g., "dashboardpage", "uploadpage")</param>
    /// <param name="parameters">Optional dictionary of query parameters</param>
    /// <returns>Task representing the navigation operation</returns>
    Task NavigateToAsync(string route, IDictionary<string, object>? parameters = null);

    /// <summary>
    /// Navigates back to the previous page in the navigation stack.
    /// </summary>
    /// <returns>Task representing the navigation operation</returns>
    Task GoBackAsync();

    /// <summary>
    /// Determines if navigation back is possible.
    /// </summary>
    /// <returns>True if there is a previous page to navigate back to; otherwise false</returns>
    bool CanGoBack();
}
