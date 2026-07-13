using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using System.Linq;
using System.Threading.Tasks;
using MotorcycleRAG.MobileApp.Configuration;

namespace MotorcycleRAG.MobileApp.Services;

public class AuthenticationService : IAuthenticationService
{
    private readonly IPublicClientApplication _pca;
    private readonly string[] _scopes;

    public AuthenticationService(
        IPublicClientApplication publicClientApplication,
        IOptions<AuthenticationOptions> authenticationOptions)
    {
        ArgumentNullException.ThrowIfNull(publicClientApplication);
        ArgumentNullException.ThrowIfNull(authenticationOptions);
        _pca = publicClientApplication;
        _scopes = authenticationOptions.Value.Scopes;
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
