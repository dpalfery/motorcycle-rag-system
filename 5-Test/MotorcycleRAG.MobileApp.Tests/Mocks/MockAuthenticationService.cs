using System.Threading.Tasks;
using Microsoft.Identity.Client;
using MotorcycleRAG.MobileApp.Services;

namespace MotorcycleRAG.MobileApp.Tests.Mocks;

public class MockAuthenticationService : IAuthenticationService
{
    public bool IsSignedIn { get; set; }
    public string? AccessToken { get; set; } = "mock-token";

    public Task<AuthenticationResult?> SignInAsync()
    {
        IsSignedIn = true;
        return Task.FromResult<AuthenticationResult?>(null); 
    }

    public Task SignOutAsync()
    {
        IsSignedIn = false;
        return Task.CompletedTask;
    }

    public Task<string?> GetAccessTokenAsync()
    {
        return Task.FromResult(AccessToken);
    }
}
