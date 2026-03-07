using Microsoft.Extensions.Logging;

namespace MotorcycleRAG.Admin.Services;

/// <summary>
/// Demo/stub implementation of IAdminAuthService for use when authentication is not configured.
/// All authentication methods return false or empty values.
/// Used to allow the app to start without requiring auth configuration.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1812: Avoid uninstantiated internal classes",
    Justification = "Instantiated by dependency injection")]
internal sealed class DemoAdminAuthService : IAdminAuthService
{
    private readonly ILogger<DemoAdminAuthService> _logger;

    internal DemoAdminAuthService(ILogger<DemoAdminAuthService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogWarning(
            "Using DemoAdminAuthService - authentication is not configured. " +
            "Navigate to Settings to configure authentication credentials.");
    }

    /// <inheritdoc/>
    public Task<bool> SignInAsync()
    {
        _logger.LogWarning("SignInAsync called on DemoAdminAuthService - authentication not configured");
        return Task.FromResult(false);
    }

    /// <inheritdoc/>
    public Task SignOutAsync()
    {
        _logger.LogDebug("SignOutAsync called on DemoAdminAuthService - no-op");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<string?> GetAccessTokenAsync()
    {
        _logger.LogWarning("GetAccessTokenAsync called on DemoAdminAuthService - authentication not configured");
        return Task.FromResult<string?>(null);
    }

    /// <inheritdoc/>
    public bool IsSignedIn()
    {
        return false;
    }

    /// <inheritdoc/>
    public string? UserDisplayName => "Not Configured";

    /// <inheritdoc/>
    public Task<IEnumerable<string>> GetUserRolesAsync()
    {
        return Task.FromResult<IEnumerable<string>>(Array.Empty<string>());
    }

    /// <inheritdoc/>
    public Task<bool> IsAuthorizedAdminAsync()
    {
        return Task.FromResult(false);
    }

    /// <inheritdoc/>
    public bool IsAuthenticated => false;
}
