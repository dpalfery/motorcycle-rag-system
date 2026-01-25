using Microsoft.Extensions.Configuration;
using Microsoft.Identity.Client;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MotorcycleRAG.MobileApp.Services;

public class AuthenticationService : IAuthenticationService
{
    private readonly IPublicClientApplication _pca;
    private readonly string[] _scopes;

    public AuthenticationService(IConfiguration configuration)
    {
        var authSettings = configuration.GetSection("Authentication");

        // Try environment variables first, fall back to configuration
        var clientId = Environment.GetEnvironmentVariable("MCR_MOBILE_CLIENT_ID")
            ?? authSettings["ClientId"];
        var tenantId = Environment.GetEnvironmentVariable("MCR_MOBILE_TENANT_ID")
            ?? authSettings["TenantId"];
        var redirectUri = Environment.GetEnvironmentVariable("MCR_MOBILE_REDIRECT_URI")
            ?? authSettings["RedirectUri"];

        // Validate required settings
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException(
                "Mobile authentication ClientId is not configured. " +
                "Set MCR_MOBILE_CLIENT_ID environment variable or configure Authentication:ClientId in appsettings.json");
        }

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new InvalidOperationException(
                "Mobile authentication TenantId is not configured. " +
                "Set MCR_MOBILE_TENANT_ID environment variable or configure Authentication:TenantId in appsettings.json");
        }

        // Default scopes if not provided in config
        _scopes = authSettings.GetSection("Scopes").Get<string[]>() ?? new[] { "api://motorcyclerag-api/read", "api://motorcyclerag-api/chat" };

        var builder = PublicClientApplicationBuilder.Create(clientId)
            .WithRedirectUri(redirectUri ?? "msauth://com.companyname.motorcyclerag.mobileapp");

        if (!string.IsNullOrEmpty(tenantId))
        {
            builder = builder.WithAuthority(AzureCloudInstance.AzurePublic, tenantId);
        }

#if ANDROID
        builder = builder.WithParentActivityOrWindow(() => Platform.CurrentActivity);
#endif

        _pca = builder.Build();
    }

    public async Task<bool> SignInAsync()
    {
        try
        {
            var accounts = await _pca.GetAccountsAsync();
            var firstAccount = accounts.FirstOrDefault();
            AuthenticationResult? result = null;

            if (firstAccount != null)
            {
                result = await _pca.AcquireTokenSilent(_scopes, firstAccount).ExecuteAsync();
            }
            else
            {
                result = await _pca.AcquireTokenInteractive(_scopes)
                                    .WithUseEmbeddedWebView(false) // Use system browser per spec
                                    .ExecuteAsync();
            }

            // Validate token is not expired (with 1-minute buffer)
            if (result != null && result.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(1))
            {
                return true;
            }

            return false;
        }
        catch (MsalUiRequiredException)
        {
            // User needs interactive sign-in
            return false;
        }
        catch (MsalException)
        {
            // Authentication error
            return false;
        }
    }

    public async Task SignOutAsync()
    {
        var accounts = await _pca.GetAccountsAsync();
        foreach (var account in accounts)
        {
            await _pca.RemoveAsync(account);
        }
    }

    public async Task<string?> GetAccessTokenAsync()
    {
        try
        {
            var accounts = await _pca.GetAccountsAsync();
            var firstAccount = accounts.FirstOrDefault();

            if (firstAccount == null)
            {
                return null;
            }

            var result = await _pca.AcquireTokenSilent(_scopes, firstAccount).ExecuteAsync();
            return result.AccessToken;
        }
        catch (MsalUiRequiredException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
