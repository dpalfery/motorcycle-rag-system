using System.Security.Claims;
using FluentAssertions;
using MotorcycleRAG.API.Extensions;
using Xunit;

namespace MotorcycleRAG.UnitTests.Presentation.API.Extensions;

public class ClaimsPrincipalExtensionsTests
{
    [Fact]
    public void HasScope_NullUser_ReturnsFalse()
    {
        // Arrange
        ClaimsPrincipal? user = null;

        // Act
        var result = user!.HasScope("read");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void HasScope_MissingClaim_ReturnsFalse()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity());

        // Act
        var result = user.HasScope("read");

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("read", "read", true)]
    [InlineData("read write", "read", true)]
    [InlineData("write read", "read", true)]
    [InlineData("write", "read", false)]
    [InlineData("READ", "read", true)]
    public void HasScope_VariousScopes_ReturnsExpectedResult(string claimValue, string requiredScope, bool expected)
    {
        // Arrange
        var claims = new[] { new Claim("scp", claimValue) };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims));

        // Act
        var result = user.HasScope(requiredScope);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void HasScope_AlternativeClaimType_ReturnsTrue()
    {
        // Arrange
        var claims = new[] { new Claim("http://schemas.microsoft.com/identity/claims/scope", "read") };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims));

        // Act
        var result = user.HasScope("read");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void HasAnyRole_NullUser_ReturnsFalse()
    {
        // Arrange
        ClaimsPrincipal? user = null;

        // Act
        var result = user!.HasAnyRole("Admin");

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(ClaimTypes.Role, "Admin", "Admin", true)]
    [InlineData("roles", "Admin", "Admin", true)]
    [InlineData(ClaimTypes.Role, "User", "Admin", false)]
    [InlineData("roles", "ADMIN", "admin", true)]
    public void HasAnyRole_VariousRoles_ReturnsExpectedResult(string claimType, string claimValue, string requiredRole, bool expected)
    {
        // Arrange
        var claims = new[] { new Claim(claimType, claimValue) };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims));

        // Act
        var result = user.HasAnyRole(requiredRole);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void HasAnyRole_MultipleRoles_ReturnsTrueIfAnyMatch()
    {
        // Arrange
        var claims = new[] 
        { 
            new Claim(ClaimTypes.Role, "User"),
            new Claim("roles", "Admin")
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims));

        // Act
        var result = user.HasAnyRole("Admin", "SuperUser");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsAuthorizedClient_NullUser_ReturnsFalse()
    {
        // Arrange
        ClaimsPrincipal? user = null;

        // Act
        var result = user!.IsAuthorizedClient("client-id");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsAuthorizedClient_EmptyExpectedId_ReturnsFalse()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity());

        // Act
        var result = user.IsAuthorizedClient("");

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("client-123", "client-123", true)]
    [InlineData("CLIENT-123", "client-123", true)]
    [InlineData("client-456", "client-123", false)]
    public void IsAuthorizedClient_MatchesClientId_ReturnsExpectedResult(string actualId, string expectedId, bool expected)
    {
        // Arrange
        var claims = new[] { new Claim("azp", actualId) };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims));

        // Act
        var result = user.IsAuthorizedClient(expectedId);

        // Assert
        result.Should().Be(expected);
    }
}
