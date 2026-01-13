using Microsoft.Identity.Client;
using System.Security.Claims;
using Microsoft.Extensions.Logging;

namespace MotorcycleRAG.Admin.Services;

/// <summary>
/// Admin authentication service using Microsoft Authentication Library (MSAL)
/// Implements device code flow for desktop applications
/// </summary>
internal class AdminAuthService : IAdminAuthService, IDisposable
{
    private readonly IPublicClientApplication _msalClient;
    private readonly string[] _scopes;
    private AuthenticationResult? _currentAuthResult;
    private DateTime _tokenExpiresAt = DateTime.MinValue;
    private readonly ILogger<AdminAuthService> _logger;
    private readonly SemaphoreSlim _tokenRefreshLock = new SemaphoreSlim(1, 1);

    internal AdminAuthService(string clientId, string authority, string[] scopes, ILogger<AdminAuthService> logger, IPublicClientApplication? msalClient)
    {
        if (string.IsNullOrEmpty(clientId))
            throw new ArgumentException("Client ID cannot be null or empty", nameof(clientId));
        if (string.IsNullOrEmpty(authority))
            throw new ArgumentException("Authority cannot be null or empty", nameof(authority));
        if (scopes == null || scopes.Length == 0)
            throw new ArgumentException("Scopes cannot be null or empty", nameof(scopes));

        _scopes = scopes;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _msalClient = msalClient ??
            PublicClientApplicationBuilder
                .Create(clientId)
                .WithAuthority(new Uri(authority))
                .WithDefaultRedirectUri()
                .Build();
    }

    internal AdminAuthService(string clientId, string authority, string[] scopes, ILogger<AdminAuthService> logger)
        : this(clientId, authority, scopes, logger, msalClient: null)
    {
    }

    /// <summary>
    /// Checks if the current cached token has expired
    /// </summary>
    private bool IsTokenExpired()
    {
        return DateTime.UtcNow >= _tokenExpiresAt;
    }

    /// <summary>
    /// Gets a valid cached token or acquires a new one if expired
    /// Thread-safe implementation using SemaphoreSlim to prevent race conditions
    /// </summary>
    private async Task<AuthenticationResult?> GetValidTokenAsync(CancellationToken cancellationToken = default)
    {
        // Quick check without lock (optimization - avoids lock contention for valid tokens)
        if (_currentAuthResult != null && !IsTokenExpired())
        {
            return _currentAuthResult;
        }

        // Acquire lock for safe refresh
        await _tokenRefreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring lock (another thread may have refreshed token)
            if (_currentAuthResult != null && !IsTokenExpired())
            {
                return _currentAuthResult;
            }

            // Try to acquire token silently
            var accounts = await _msalClient.GetAccountsAsync().ConfigureAwait(false);
            if (accounts.Any())
            {
                try
                {
                    _currentAuthResult = await _msalClient
                        .AcquireTokenSilent(_scopes, accounts.FirstOrDefault())
                        .ExecuteAsync(cancellationToken).ConfigureAwait(false);
                    _tokenExpiresAt = _currentAuthResult.ExpiresOn.UtcDateTime;
                    _logger.LogDebug("Token refreshed successfully. Expires at: {ExpiresOn}", _currentAuthResult.ExpiresOn);
                    return _currentAuthResult;
                }
                catch (MsalUiRequiredException ex)
                {
                    // User needs to sign in again - expected in some scenarios
                    _logger.LogWarning(ex, "Token refresh requires interactive signin");
                    return null;
                }
            }

            _logger.LogWarning("No cached accounts available for silent token acquisition");
            return null;
        }
        finally
        {
            _tokenRefreshLock.Release();
        }
    }

    /// <summary>
    /// Signs in the user using device code flow
    /// </summary>
    public async Task<bool> SignInAsync()
    {
        try
        {
            // Try silent sign-in first
            var accounts = await _msalClient.GetAccountsAsync().ConfigureAwait(false);
            if (accounts.Any())
            {
                try
                {
                    _currentAuthResult = await _msalClient
                        .AcquireTokenSilent(_scopes, accounts.FirstOrDefault())
                        .ExecuteAsync().ConfigureAwait(false);
                    _tokenExpiresAt = _currentAuthResult.ExpiresOn.UtcDateTime;
                    return true;
                }
                catch (MsalUiRequiredException)
                {
                    // Fall through to interactive sign-in
                }
            }

            // Interactive sign-in with device code flow
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

            _currentAuthResult = await _msalClient
                .AcquireTokenWithDeviceCode(_scopes, deviceCodeResult =>
                {
                    // Display the device code to the user - log key information only
                    _logger.LogInformation("Device code flow initiated. ExpiresOn: {Expires}, VerificationUrl: {Url}, CodeLength: {CodeLength}, Message: {Message}",
                        deviceCodeResult.ExpiresOn, deviceCodeResult.VerificationUrl, deviceCodeResult.DeviceCode?.Length ?? 0, deviceCodeResult.Message);

                    // On Windows, we can also show this in the UI
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        var window = Application.Current?.Windows is { Count: > 0 } windows ? windows[0] : null;
                        window?.Page?.DisplayAlertAsync(
                            "Sign In",
                            deviceCodeResult.Message,
                            "OK");
                    });

                    return Task.CompletedTask;
                })
                .ExecuteAsync(cts.Token).ConfigureAwait(false);

            if (_currentAuthResult != null)
            {
                _tokenExpiresAt = _currentAuthResult.ExpiresOn.UtcDateTime;
            }

            return _currentAuthResult != null;
        }
        catch (MsalException ex)
        {
            _logger.LogError(ex, "Authentication failed");
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var window = Application.Current?.Windows is { Count: > 0 } windows ? windows[0] : null;
                if (window?.Page != null)
                {
                    await window.Page.DisplayAlertAsync(
                        "Authentication Error",
                        $"Failed to sign in: {ex.Message}",
                        "OK").ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
            return false;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "Device code authentication timed out after 5 minutes");
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var window = Application.Current?.Windows is { Count: > 0 } windows ? windows[0] : null;
                if (window?.Page != null)
                {
                    await window.Page.DisplayAlertAsync(
                        "Authentication Timeout",
                        "Device code sign-in timed out after 5 minutes. Please try again.",
                        "OK").ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
            return false;
        }
    }

    /// <summary>
    /// Signs out the current user
    /// </summary>
    public async Task SignOutAsync()
    {
        var accounts = await _msalClient.GetAccountsAsync().ConfigureAwait(false);
        foreach (var account in accounts)
        {
            await _msalClient.RemoveAsync(account).ConfigureAwait(false);
        }
        _currentAuthResult = null;
        _tokenExpiresAt = DateTime.MinValue;
    }

    /// <summary>
    /// Gets a valid access token, refreshing if necessary
    /// Returns null if token cannot be acquired (e.g., interactive signin required)
    /// Callers should handle null and offer to re-authenticate
    /// </summary>
    public async Task<string?> GetAccessTokenAsync()
    {
        var validToken = await GetValidTokenAsync().ConfigureAwait(false);

        if (validToken == null)
        {
            _logger.LogWarning("Failed to acquire valid token - interactive signin may be required");
            return null;
        }

        return validToken.AccessToken;
    }

    /// <summary>
    /// Checks if the user is currently signed in
    /// </summary>
    public bool IsSignedIn()
    {
        var signedIn = _currentAuthResult != null && _currentAuthResult.ExpiresOn > DateTimeOffset.UtcNow;
        _logger?.LogDebug("IsSignedIn: {SignedIn}, ExpiresOn: {Expires}", signedIn, _currentAuthResult?.ExpiresOn);
        return signedIn;
    }

    /// <summary>
    /// Gets a value indicating whether the user is currently authenticated
    /// </summary>
    public bool IsAuthenticated => IsSignedIn();

    /// <summary>
    /// Gets the current user's display name
    /// </summary>
    public string? UserDisplayName
    {
        get
        {
            if (_currentAuthResult?.Account == null)
                return null;

            var name = _currentAuthResult.Account.Username;
            _logger?.LogDebug("UserDisplayName property accessed: {Name}", name);
            return name;
        }
    }

    /// <summary>
    /// Gets the current user's roles from token claims
    /// </summary>
    public async Task<IEnumerable<string>> GetUserRolesAsync()
    {
        if (_currentAuthResult == null)
            return Enumerable.Empty<string>();

        // Parse the ID token to get claims
        var idToken = _currentAuthResult.IdToken;
        if (string.IsNullOrEmpty(idToken))
            return Enumerable.Empty<string>();

        try
        {
            // Extract roles from claims
            var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
            var token = handler.ReadJwtToken(idToken);

            var roleClaims = token.Claims
                .Where(c => c.Type == "roles" || c.Type == ClaimTypes.Role)
                .Select(c => c.Value)
                .ToList();

            return roleClaims;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to parse user roles from token");
            return Enumerable.Empty<string>();
        }
    }


    /// <summary>
    /// Disposes the SemaphoreSlim used for token refresh synchronization
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Protected Dispose implementation
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tokenRefreshLock?.Dispose();
        }
    }

    /// <summary>
    /// Finalizer for cleanup if Dispose was not called
    /// </summary>
    ~AdminAuthService()
    {
        Dispose(false);
    }
}
