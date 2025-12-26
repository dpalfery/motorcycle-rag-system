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
        var clientId = authSettings["ClientId"];
        var tenantId = authSettings["TenantId"];
        var redirectUri = authSettings["RedirectUri"];

        // Default scopes if not provided in config
        _scopes = authSettings.GetSection("Scopes").Get<string[]>() ?? new[] { "User.Read" };

        var builder = PublicClientApplicationBuilder.Create(clientId)
            .WithRedirectUri(redirectUri);

        if (!string.IsNullOrEmpty(tenantId))
        {
            builder = builder.WithAuthority(AzureCloudInstance.AzurePublic, tenantId);
        }

#if ANDROID
        builder = builder.WithParentActivityOrWindow(() => Platform.CurrentActivity);
#endif

        _pca = builder.Build();
    }

    public async Task<AuthenticationResult?> SignInAsync()
    {
        try
        {
            var accounts = await _pca.GetAccountsAsync();
            var firstAccount = accounts.FirstOrDefault();

            if (firstAccount != null)
            {
                return await _pca.AcquireTokenSilent(_scopes, firstAccount).ExecuteAsync();
            }

            return await _pca.AcquireTokenInteractive(_scopes)
                                .ExecuteAsync();
        }
        catch (MsalException)
        {
            // In a real app, we might want to log this or handle specific error codes
            throw;
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
