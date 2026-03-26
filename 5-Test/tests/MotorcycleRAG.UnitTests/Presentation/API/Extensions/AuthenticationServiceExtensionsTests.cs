using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MotorcycleRAG.API;

namespace MotorcycleRAG.UnitTests.Presentation.API.Extensions;

public class AuthenticationServiceExtensionsTests
{
    [Fact]
    public void AddDualIssuerJwtBearer_GuidAudience_AlsoAcceptsNamedApplicationIdUri()
    {
        // Arrange
        const string clientId = "9221a8e3-4d10-45d4-ae8c-76acd4a5d631";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Authentication:Issuers:Workforce", "https://login.microsoftonline.com/tenant-123/v2.0" },
                { "Authentication:Audience", clientId },
                { "AzureAd:ClientId", clientId }
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        var authenticationBuilder = services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme);

        // Act
        InvokeAddDualIssuerJwtBearer(authenticationBuilder, configuration);

        using var serviceProvider = services.BuildServiceProvider();
        var optionsMonitor = serviceProvider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>();
        var options = optionsMonitor.Get(JwtBearerDefaults.AuthenticationScheme);

        // Assert
        options.TokenValidationParameters.ValidAudiences.Should().NotBeNull();
        options.TokenValidationParameters.ValidAudiences.Should().BeEquivalentTo(new[]
        {
            clientId,
            $"api://{clientId}",
            "api://motorcyclerag-api"
        });
    }

    private static AuthenticationBuilder InvokeAddDualIssuerJwtBearer(
        AuthenticationBuilder builder,
        IConfiguration configuration)
    {
        var extensionType = typeof(Program).Assembly.GetType(
            "MotorcycleRAG.API.Extensions.AuthenticationServiceExtensions",
            throwOnError: true)!;
        var method = extensionType.GetMethod(
            "AddDualIssuerJwtBearer",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        var result = method.Invoke(null, [builder, configuration, NullLogger.Instance]);
        result.Should().NotBeNull();
        result.Should().BeOfType<AuthenticationBuilder>();

        return (AuthenticationBuilder)result!;
    }
}
