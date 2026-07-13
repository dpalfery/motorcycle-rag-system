using MotorcycleRAG.MobileApp.Configuration;
using FluentAssertions;

namespace MotorcycleRAG.MobileApp.Tests.Configuration;

public sealed class AuthenticationOptionsValidatorTests
{
    private readonly AuthenticationOptionsValidator _validator = new();

    [Fact]
    public void Validate_WhenOptionsAreComplete_Succeeds()
    {
        var result = _validator.Validate(null, new AuthenticationOptions
        {
            ClientId = "client-id",
            TenantId = "tenant-id",
            RedirectUri = "msauth.com.motorcyclerag.mobile://auth",
            Scopes = ["api://motorcyclerag-api/read"],
        });

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WhenScopesAreMissing_ReturnsFailure()
    {
        var result = _validator.Validate(null, new AuthenticationOptions
        {
            ClientId = "client-id",
            TenantId = "tenant-id",
            RedirectUri = "msauth.com.motorcyclerag.mobile://auth",
        });

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(failure => failure.Contains("Scopes"));
    }

    [Fact]
    public void Validate_WhenClientIdIsMissing_ReturnsFailure()
    {
        var result = _validator.Validate(null, new AuthenticationOptions
        {
            TenantId = "tenant-id",
            RedirectUri = "msauth.com.motorcyclerag.mobile://auth",
            Scopes = ["api://motorcyclerag-api/read"],
        });

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(failure => failure.Contains("ClientId"));
    }

    [Fact]
    public void Validate_WhenTenantIdIsMissing_ReturnsFailure()
    {
        var result = _validator.Validate(null, new AuthenticationOptions
        {
            ClientId = "client-id",
            RedirectUri = "msauth.com.motorcyclerag.mobile://auth",
            Scopes = ["api://motorcyclerag-api/read"],
        });

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(failure => failure.Contains("TenantId"));
    }

    [Fact]
    public void Validate_WhenRedirectUriIsInvalid_ReturnsFailure()
    {
        var result = _validator.Validate(null, new AuthenticationOptions
        {
            ClientId = "client-id",
            TenantId = "tenant-id",
            RedirectUri = "not a uri",
            Scopes = ["api://motorcyclerag-api/read"],
        });

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(failure => failure.Contains("RedirectUri"));
    }
}
