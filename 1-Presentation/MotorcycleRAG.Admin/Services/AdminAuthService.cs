using Microsoft.Identity.Client;
using System.Security.Claims;

namespace MotorcycleRAG.Admin.Services;

/// <summary>
/// Admin authentication service using Microsoft Authentication Library (MSAL)
/// Implements device code flow for desktop applications
/// </summary>
public class AdminAuthService : IAdminAuthService
{
    private readonly IPublicClientApplication _msalClient;
    private readonly string[] _scopes;
    private AuthenticationResult? _currentAuthResult;

    public AdminAuthService(string clientId, string authority, string[] scopes)
    {
        if (string.IsNullOrEmpty(clientId))
            throw new ArgumentException("Client ID cannot be null or empty", nameof(clientId));
        if (string.IsNullOrEmpty(authority))
            throw new ArgumentException("Authority cannot be null or empty", nameof(authority));
        if (scopes == null || scopes.Length == 0)
            throw new ArgumentException("Scopes cannot be null or empty", nameof(scopes));

        _scopes = scopes;

        _msalClient = PublicClientApplicationBuilder
            .Create(clientId)
            .WithAuthority(authority)
            .WithDefaultRedirectUri()
            .Build();
    }

    /// <summary>
    /// Signs in the user using device code flow
    /// </summary>
    public async Task<bool> SignInAsync()
    {
        try
        {
            // Try silent sign-in first
            var accounts = await _msalClient.GetAccountsAsync();
            if (accounts.Any())
            {
                try
                {
                    _currentAuthResult = await _msalClient
                        .AcquireTokenSilent(_scopes, accounts.FirstOrDefault())
                        .ExecuteAsync();
                    return true;
                }
                catch (MsalUiRequiredException)
                {
                    // Fall through to interactive sign-in
                }
            }

            // Interactive sign-in with device code flow
            _currentAuthResult = await _msalClient
                .AcquireTokenWithDeviceCode(_scopes, deviceCodeResult =>
                {
                    // Display the device code to the user
                    Console.WriteLine(deviceCodeResult.Message);
                    
                    // On Windows, we can also show this in the UI
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        var window = Application.Current?.Windows?.FirstOrDefault();
                        window?.Page?.DisplayAlertAsync(
                            "Sign In",
                            deviceCodeResult.Message,
                            "OK");
                    });
                    
                    return Task.CompletedTask;
                })
                .ExecuteAsync();

            return _currentAuthResult != null;
        }
        catch (MsalException ex)
        {
            Console.WriteLine($"Authentication failed: {ex.Message}");
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var window = Application.Current?.Windows?.FirstOrDefault();
                if (window?.Page != null)
                {
                    await window.Page.DisplayAlertAsync(
                        "Authentication Error",
                        $"Failed to sign in: {ex.Message}",
                        "OK");
                }
            });
            return false;
        }
    }

    /// <summary>
    /// Signs out the current user
    /// </summary>
    public async Task SignOutAsync()
    {
        var accounts = await _msalClient.GetAccountsAsync();
        foreach (var account in accounts)
        {
            await _msalClient.RemoveAsync(account);
        }
        _currentAuthResult = null;
    }

    /// <summary>
    /// Gets a valid access token, refreshing if necessary
    /// </summary>
    public async Task<string?> GetAccessTokenAsync()
    {
        // Check if current token is still valid
        if (_currentAuthResult != null && _currentAuthResult.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return _currentAuthResult.AccessToken;
        }

        // Try to acquire token silently
        var accounts = await _msalClient.GetAccountsAsync();
        if (accounts.Any())
        {
            try
            {
                _currentAuthResult = await _msalClient
                    .AcquireTokenSilent(_scopes, accounts.FirstOrDefault())
                    .ExecuteAsync();
                return _currentAuthResult.AccessToken;
            }
            catch (MsalUiRequiredException)
            {
                // User needs to sign in again
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if the user is currently signed in
    /// </summary>
    public bool IsSignedIn()
    {
        return _currentAuthResult != null && _currentAuthResult.ExpiresOn > DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Gets the current user's display name
    /// </summary>
    public string? GetUserDisplayName()
    {
        if (_currentAuthResult?.Account == null)
            return null;

        return _currentAuthResult.Account.Username;
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
        catch
        {
            return Enumerable.Empty<string>();
        }
    }
}
