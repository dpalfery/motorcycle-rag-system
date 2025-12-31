using System.Threading.Tasks;
using Microsoft.Identity.Client;

namespace MotorcycleRAG.MobileApp.Services;

public interface IAuthenticationService
{
    Task<bool> SignInAsync();
    Task SignOutAsync();
    Task<string?> GetAccessTokenAsync();
}
