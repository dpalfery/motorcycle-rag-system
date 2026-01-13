namespace MotorcycleRAG.Admin.Services;

/// <summary>
/// Implementation of INavigationService wrapping MAUI Shell navigation.
/// This allows ViewModels to remain decoupled from Shell.Current and testable.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812: Avoid uninstantiated internal classes", Justification = "Instantiated by dependency injection")]
internal class NavigationService : INavigationService
{
    /// <summary>
    /// Navigates to the specified route with optional query parameters.
    /// </summary>
    /// <param name="route">The route to navigate to</param>
    /// <param name="parameters">Optional query parameters</param>
    public async Task NavigateToAsync(string route)
    {
        await NavigateToAsync(route, parameters: null);
    }

    /// <summary>
    /// Navigates to the specified route with query parameters.
    /// </summary>
    /// <param name="route">The route to navigate to</param>
    /// <param name="parameters">Query parameters</param>
    public async Task NavigateToAsync(string route, IDictionary<string, object>? parameters)
    {
        try
        {
            // Validate Shell is available
            if (Shell.Current == null)
            {
                throw new InvalidOperationException("Shell.Current is null - navigation not available during app initialization");
            }

            // Build the navigation URI with query parameters if provided
            var navigationUri = route;
            if (parameters != null && parameters.Count > 0)
            {
                // SECURITY: Validate parameter names and encode values to prevent injection
                var queryParts = new List<string>();
                foreach (var kvp in parameters)
                {
                    // Validate parameter name is not empty and contains only alphanumeric and underscore
                    if (string.IsNullOrWhiteSpace(kvp.Key) || !System.Text.RegularExpressions.Regex.IsMatch(kvp.Key, @"^[a-zA-Z0-9_]+$"))
                    {
                        throw new ArgumentException($"Parameter name '{kvp.Key}' is invalid. Names must contain only alphanumeric characters and underscores.");
                    }

                    var value = kvp.Value?.ToString() ?? string.Empty;
                    queryParts.Add($"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(value)}");
                }

                var queryString = string.Join("&", queryParts);

                // Validate query string doesn't exceed reasonable length
                if (queryString.Length > 2048)
                {
                    throw new ArgumentException($"Query string exceeds maximum length of 2048 characters. Length: {queryString.Length}");
                }

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
            // Validate Shell is available
            if (Shell.Current?.Navigation?.NavigationStack == null)
            {
                return; // Can't navigate back if shell is not initialized
            }

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
        try
        {
            var navStack = Shell.Current?.Navigation?.NavigationStack;
            return navStack != null && navStack.Count > 1;
        }
        catch
        {
            // If any exception occurs during navigation check, assume can't go back
            return false;
        }
    }
}
