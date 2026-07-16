using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.API.Services;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.UnitTests.Presentation.API.Services;

public sealed class CurrentUserServiceCoverageTests
{
    [Fact]
    public void PropertiesAndRoles_ReadStandardAndAlternateClaims()
    {
        var service = Create(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "name-id"), new Claim("sub", "subject"), new Claim("iss", "issuer"),
            new Claim("email", "alternate@example.test"), new Claim("name", "Name"), new Claim("given_name", "First"),
            new Claim("family_name", "Last"), new Claim("idp", "microsoft"), new Claim("oid", "object"),
            new Claim("appid", "app"), new Claim("roles", "Reader"), new Claim(ClaimTypes.Role, "StandardReader")], "test"));

        service.UserId.Should().Be("name-id"); service.Subject.Should().Be("subject"); service.Issuer.Should().Be("issuer");
        service.Email.Should().Be("alternate@example.test"); service.DisplayName.Should().Be("Name");
        service.FirstName.Should().Be("First"); service.LastName.Should().Be("Last"); service.AuthProvider.Should().Be("microsoft");
        service.ProviderUserId.Should().Be("object"); service.ObjectId.Should().Be("object"); service.AuthorizedParty.Should().Be("app");
        service.IsAuthenticated.Should().BeTrue(); service.IsInRole("Reader").Should().BeTrue(); service.IsInRole("StandardReader").Should().BeTrue(); service.IsInRole("Missing").Should().BeFalse();
        service.GetClaims().Should().HaveCount(12);
    }

    [Fact]
    public void NoContextOrIdentity_ReturnsSafeDefaults()
    {
        var accessor = new HttpContextAccessor();
        var provisioning = Mock.Of<IUserProvisioningService>();
        var service = new CurrentUserService(accessor, provisioning, NullLogger<CurrentUserService>.Instance);

        service.UserId.Should().BeNull(); service.Email.Should().BeNull(); service.IsAuthenticated.Should().BeFalse();
        service.IsInRole("Reader").Should().BeFalse(); service.IsInRole(" ").Should().BeFalse(); service.GetClaims().Should().BeEmpty();
    }

    [Fact]
    public void PrincipalWithoutAnIdentity_IsUnauthenticated()
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal() }
        };
        var service = new CurrentUserService(accessor, Mock.Of<IUserProvisioningService>(), NullLogger<CurrentUserService>.Instance);

        service.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task ManagedUser_UsesMicrosoftFallbackAndManagedId()
    {
        var provisioning = new Mock<IUserProvisioningService>();
        provisioning.Setup(x => x.ReconcileApprovedUserAsync("", "", "rider@example.test", null, null, null,
                IdentityProvider.Microsoft, null, null))
            .ReturnsAsync(new UserDTO { Id = "managed" });
        var service = Create(new ClaimsIdentity([new Claim("preferred_username", "rider@example.test")], "test"), provisioning.Object);

        (await service.GetManagedUserIdAsync()).Should().Be("managed");
    }

    [Fact]
    public async Task ManagedUser_Unauthenticated_ReturnsNull()
    {
        var service = Create(new ClaimsIdentity());
        (await service.GetManagedUserAsync()).Should().BeNull();
        (await service.GetManagedUserIdAsync()).Should().BeNull();
    }

    [Fact]
    public void Constructor_NullDependencies_Throw()
    {
        var accessor = new HttpContextAccessor(); var provisioning = Mock.Of<IUserProvisioningService>();
        Assert.Throws<ArgumentNullException>(() => new CurrentUserService(null!, provisioning, NullLogger<CurrentUserService>.Instance));
        Assert.Throws<ArgumentNullException>(() => new CurrentUserService(accessor, null!, NullLogger<CurrentUserService>.Instance));
        Assert.Throws<ArgumentNullException>(() => new CurrentUserService(accessor, provisioning, null!));
    }

    private static CurrentUserService Create(ClaimsIdentity identity, IUserProvisioningService? provisioning = null)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } };
        return new CurrentUserService(accessor, provisioning ?? Mock.Of<IUserProvisioningService>(), NullLogger<CurrentUserService>.Instance);
    }
}
