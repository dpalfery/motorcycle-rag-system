namespace MotorcycleRAG.Admin.Services;

/// <summary>
/// Interface for admin authentication services
/// </summary>
internal interface IAdminAuthService {
    /// <summary>
    /// Signs in the user interactively
    /// </summary>
    Task<bool> SignInAsync();

    /// <summary>
    /// Signs out the current user
    /// </summary>
    Task SignOutAsync();

    /// <summary>
    /// Gets a valid access token, refreshing if necessary
    /// </summary>
    Task<string?> GetAccessTokenAsync();

    /// <summary>
    /// Checks if the user is currently signed in
    /// </summary>
    bool IsSignedIn();

    /// <summary>
    /// Gets the current user's display name
    /// </summary>
    string? UserDisplayName { get; }

    /// <summary>
    /// Gets the current user's roles
    /// </summary>
    Task<IEnumerable<string>> GetUserRolesAsync();

    /// <summary>
    /// Checks if the user is authorized as an administrator.
    /// In DEBUG mode, this may return true even without specific roles to facilitate development.
    /// </summary>
    Task<bool> IsAuthorizedAdminAsync();

    /// <summary>
    /// Gets a value indicating whether the user is currently authenticated
    /// </summary>
    bool IsAuthenticated { get; }
}
