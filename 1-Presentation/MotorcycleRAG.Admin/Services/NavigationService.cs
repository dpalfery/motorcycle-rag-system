namespace MotorcycleRAG.Admin.Services;

/// <summary>
/// Implementation of INavigationService wrapping MAUI Shell navigation.
/// This allows ViewModels to remain decoupled from Shell.Current and testable.
/// </summary>
public class NavigationService : INavigationService
{
    /// <summary>
    /// Navigates to the specified route with optional query parameters.
    /// </summary>
    /// <param name="route">The route to navigate to</param>
    /// <param name="parameters">Optional query parameters</param>
    public async Task NavigateToAsync(string route, IDictionary<string, object>? parameters = null)
    {
        try
        {
            // Build the navigation URI with query parameters if provided
            var navigationUri = route;
            if (parameters != null && parameters.Count > 0)
            {
                var queryString = string.Join("&", parameters.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value.ToString() ?? "")}"));
                navigationUri = $"{route}?{queryString}";
            }

            await Shell.Current.GoToAsync(navigationUri);
        }
        catch (Exception ex)
        {
            // Log and rethrow - caller's responsibility to handle
            System.Diagnostics.Debug.WriteLine($"Navigation error to {route}: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Navigates back to the previous page.
    /// </summary>
    public async Task GoBackAsync()
    {
        try
        {
            if (Shell.Current.Navigation.NavigationStack.Count > 1)
            {
                await Shell.Current.GoToAsync("..");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Navigation back error: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Checks if navigation back is possible.
    /// </summary>
    public bool CanGoBack()
    {
        return Shell.Current?.Navigation?.NavigationStack?.Count > 1;
    }
}
