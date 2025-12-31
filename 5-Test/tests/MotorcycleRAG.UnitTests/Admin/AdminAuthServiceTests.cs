using Microsoft.Identity.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MotorcycleRAG.Admin.Services;
using Xunit;

namespace MotorcycleRAG.UnitTests.Admin;

public class AdminAuthServiceTests
{
    [Fact]
    public async Task IsSignedIn_ReturnsFalse_WhenNoToken()
    {
        var msal = new Mock<IPublicClientApplication>();
        msal.Setup(m => m.GetAccountsAsync()).ReturnsAsync(new List<IAccount>());

        var service = new AdminAuthService("cid", "https://login.microsoftonline.com/tenant", new[] { "scope" }, NullLogger<AdminAuthService>.Instance, msal.Object);

        var result = service.IsSignedIn();

        Assert.False(result);
    }
}
