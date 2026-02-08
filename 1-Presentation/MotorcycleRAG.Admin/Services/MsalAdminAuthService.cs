using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using System.Diagnostics;
using System.Linq;

namespace MotorcycleRAG.Admin.Services;

/// <summary>
/// Production implementation of AdminAuthService using MSAL.NET
/// Implements secure token caching and system browser authentication
/// </summary>
public class MsalAdminAuthService : IAdminAuthService {
    private readonly string _clientId;
    private readonly string _authority;
    private readonly string[] _scopes;
    private readonly ILogger<MsalAdminAuthService> _logger;
    private IPublicClientApplication? _pca;
    private const string CacheFileName = "msal_cache.dat";
    private readonly string _cacheDirectory;
    private IAccount? _currentAccount;

    public MsalAdminAuthService(
        string clientId,
        string authority,
        string[] scopes,
        ILogger<MsalAdminAuthService> logger) {
        _clientId = clientId;
        _authority = authority;
        _scopes = scopes;
        _logger = logger;

        // Store cache in user's local app data folder
        _cacheDirectory = Path.Combine(MsalCacheHelper.UserRootDirectory, "MotorcycleRAG.Admin");
    }

    private async Task<IPublicClientApplication> GetPcaAsync() {
        if (_pca != null) return _pca;

        // Build Public Client Application
#pragma warning disable S1075 // URIs should not be hardcoded
        var builder = PublicClientApplicationBuilder.Create(_clientId)
            .WithAuthority(new Uri(_authority))
            .WithRedirectUri("http://localhost") // Recommended loopback URI for desktop apps
            .WithLogging(LogMsal, Microsoft.Identity.Client.LogLevel.Info, enablePiiLogging: false);
#pragma warning restore S1075 // URIs should not be hardcoded

        _pca = builder.Build();

        // Register Token Cache
        var storageProperties = new StorageCreationPropertiesBuilder(CacheFileName, _cacheDirectory)
            .Build();

        // MsalCacheHelper handles cross-platform secure storage
        // Windows: DPAPI, Mac: KeyChain, Linux: KeyRing
        var cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties);
        cacheHelper.RegisterCache(_pca.UserTokenCache);

        return _pca;
    }

    public async Task<string?> GetAccessTokenAsync() {
        var pca = await GetPcaAsync();

        // Refresh account status
        var accounts = await pca.GetAccountsAsync();
        _currentAccount = accounts.FirstOrDefault();

        try {
            AuthenticationResult result;
            if (_currentAccount != null) {
                // Try silent acquisition first
                _logger.LogInformation("Acquiring token silently...");
                result = await pca.AcquireTokenSilent(_scopes, _currentAccount)
                    .ExecuteAsync();
            }
            else {
                // Force interactive login if no account found
                _logger.LogInformation("No cached account found, acquiring token interactively...");
                result = await pca.AcquireTokenInteractive(_scopes)
                    .WithUseEmbeddedWebView(false) // Use system browser
                                                   // Note: MSAL.NET on Windows using WAM (Windows Account Manager) or Default Browser
                                                   // works differently than direct SystemWebViewOptions configuration in older versions.
                                                   // To support custom protocol redirect URI on Windows with System Browser,
                                                   // we usually rely on proper registry/manifest configuration and let MSAL/OS handle the callback.
                                                   // The "OpenWithShell" option is not available in standard SystemWebViewOptions.
                    .ExecuteAsync();
            }

            _currentAccount = result.Account;
            return result.AccessToken;
        }
        catch (MsalUiRequiredException) {
            // Silent acquisition failed, try interactive
            try {
                _logger.LogInformation("Silent acquisition failed, acquiring token interactively...");
                var result = await pca.AcquireTokenInteractive(_scopes)
                    .WithUseEmbeddedWebView(false) // Use system browser
                    .ExecuteAsync();

                _currentAccount = result.Account;
                return result.AccessToken;
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Interactive authentication failed");
                return null;
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Authentication failed");
            return null;
        }
    }

    public async Task<bool> SignInAsync() {
        var token = await GetAccessTokenAsync();
        return !string.IsNullOrEmpty(token);
    }

    public bool IsSignedIn() {
        // Sync check is best effort based on last known state
        // For accurate state, use async checks
        return _currentAccount != null;
    }

    public bool IsAuthenticated => IsSignedIn();

    public string? UserDisplayName => _currentAccount?.Username;

    public async Task<IEnumerable<string>> GetUserRolesAsync() {
        // MSAL access tokens don't directly expose roles in the public API easily without decoding
        // For admin app logic, we rely on the API to enforce role permissions
        // This is a simplified implementation - in a real app we might decode the JWT or query Graph
        if (!IsSignedIn()) return Enumerable.Empty<string>();

        // We assume if they can sign in to this app config, they are at least an Admin candidate
        // The API will reject them if they don't have the claim
        return await Task.FromResult(new[] { "mcr-api-admin" });
    }

    public async Task<bool> IsAuthorizedAdminAsync() {
        return await SignInAsync();
    }

    public async Task SignOutAsync() {
        await LogoutAsync();
    }

    public async Task LogoutAsync() {
        var pca = await GetPcaAsync();
        var accounts = await pca.GetAccountsAsync();
        foreach (var account in accounts) {
            await pca.RemoveAsync(account);
        }
        _currentAccount = null;
        _logger.LogInformation("User logged out");
    }

    private void LogMsal(Microsoft.Identity.Client.LogLevel level, string message, bool containsPii) {
        if (containsPii) return;

        switch (level) {
            case Microsoft.Identity.Client.LogLevel.Error:
                _logger.LogError("[MSAL] {Message}", message);
                break;
            case Microsoft.Identity.Client.LogLevel.Warning:
                _logger.LogWarning("[MSAL] {Message}", message);
                break;
            case Microsoft.Identity.Client.LogLevel.Info:
                _logger.LogInformation("[MSAL] {Message}", message);
                break;
            case Microsoft.Identity.Client.LogLevel.Verbose:
                _logger.LogDebug("[MSAL] {Message}", message);
                break;
            default:
                _logger.LogTrace("[MSAL] {Message}", message);
                break;
        }
    }
}
