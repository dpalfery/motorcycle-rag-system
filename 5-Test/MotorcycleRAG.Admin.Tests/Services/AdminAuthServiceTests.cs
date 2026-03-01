using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Moq;
using MotorcycleRAG.Admin.Services;

namespace MotorcycleRAG.Admin.Tests.Services;

public class AdminAuthServiceTests
{
    private static readonly string[] DefaultScopes = ["api://test/.default"];
    // Test that reproduces the bug: passing null logger throws ArgumentNullException
    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        var act = () => new AdminAuthService(
            clientId: "test-client-id",
            authority: "https://login.microsoftonline.com/test-tenant",
            scopes: DefaultScopes,
            logger: null!);

        act.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("logger");
    }

    // Test that valid construction works (confirms the fix)
    [Fact]
    public void Constructor_WithValidLogger_DoesNotThrow()
    {
        var mockLogger = new Mock<ILogger<AdminAuthService>>();
        var mockMsalClient = new Mock<IPublicClientApplication>();

        // Setup mock to return empty accounts (prevents InitializeCache from hitting real MSAL)
        mockMsalClient.Setup(m => m.GetAccountsAsync())
            .ReturnsAsync(Array.Empty<IAccount>());
        // Setup UserTokenCache property
        mockMsalClient.Setup(m => m.UserTokenCache).Returns(Mock.Of<ITokenCache>());

        var act = () => new AdminAuthService(
            clientId: "test-client-id",
            authority: "https://login.microsoftonline.com/test-tenant",
            scopes: DefaultScopes,
            logger: mockLogger.Object,
            msalClient: mockMsalClient.Object);

        act.Should().NotThrow();
    }

    // Additional guard tests
    [Fact]
    public void Constructor_WithNullClientId_ThrowsArgumentException()
    {
        var mockLogger = new Mock<ILogger<AdminAuthService>>();

        var act = () => new AdminAuthService(
            clientId: null!,
            authority: "https://login.microsoftonline.com/test-tenant",
            scopes: DefaultScopes,
            logger: mockLogger.Object);

        act.Should().Throw<ArgumentException>()
            .And.ParamName.Should().Be("clientId");
    }

    [Fact]
    public void Constructor_WithNullAuthority_ThrowsArgumentException()
    {
        var mockLogger = new Mock<ILogger<AdminAuthService>>();

        var act = () => new AdminAuthService(
            clientId: "test-client-id",
            authority: null!,
            scopes: DefaultScopes,
            logger: mockLogger.Object);

        act.Should().Throw<ArgumentException>()
            .And.ParamName.Should().Be("authority");
    }

    [Fact]
    public void Constructor_WithNullScopes_ThrowsArgumentException()
    {
        var mockLogger = new Mock<ILogger<AdminAuthService>>();

        var act = () => new AdminAuthService(
            clientId: "test-client-id",
            authority: "https://login.microsoftonline.com/test-tenant",
            scopes: null!,
            logger: mockLogger.Object);

        act.Should().Throw<ArgumentException>()
            .And.ParamName.Should().Be("scopes");
    }
}
