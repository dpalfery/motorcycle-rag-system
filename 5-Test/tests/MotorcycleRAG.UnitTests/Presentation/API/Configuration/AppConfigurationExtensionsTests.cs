using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using MotorcycleRAG.API.Configuration;
using Xunit;

namespace MotorcycleRAG.UnitTests.Presentation.API.Configuration;

public class AppConfigurationExtensionsTests {
    [Fact]
    public void AddAzureAppConfigurationWithKeyVault_InDevelopment_SkipsRemoteAppConfiguration() {
        // Arrange
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions {
            EnvironmentName = Environments.Development
        });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["AppConfig:ConnectionString"] = "Endpoint=https://placeholder.azconfig.io;Id=test;Secret=test",
            ["AzureAd:TenantId"] = "tenant-123",
            ["AzureAd:ClientId"] = "client-456"
        });

        // Act
        var act = () => builder.AddAzureAppConfigurationWithKeyVault();

        // Assert
        act.Should().NotThrow();
        builder.Configuration[AppConfigurationExtensions.AppConfigurationEnabledKey].Should().Be(bool.FalseString);
    }

    [Fact]
    public void WithDerivedAzureAdValues_ValidInput_DerivesCorrectValues() {
        // Arrange
        var tenantId = "tenant-123";
        var clientId = "client-456";
        var inMemoryConfig = new Dictionary<string, string?>
        {
            { "AzureAd:TenantId", tenantId },
            { "AzureAd:ClientId", clientId }
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemoryConfig)
            .Build();

        // Act
        configuration.WithDerivedAzureAdValues();

        // Assert
        configuration["AzureAd:Audience"].Should().Be(clientId);
        configuration["Authentication:Audience"].Should().Be(clientId);
        configuration["Jwt:ValidAudience"].Should().Be(clientId);
        configuration["Jwt:ValidIssuer"].Should().Be($"https://login.microsoftonline.com/{tenantId}/v2.0");
        configuration["Jwt:IssuerSigningKeyUrl"].Should().Be($"https://login.microsoftonline.com/{tenantId}/discovery/v2.0/keys");
        configuration["Authentication:Issuers:Workforce"].Should().Be($"https://login.microsoftonline.com/{tenantId}/v2.0");
    }

    [Fact]
    public void WithDerivedAzureAdValues_ConfiguredAudience_PreservesConfiguredAudience() {
        // Arrange
        var tenantId = "tenant-123";
        var clientId = "client-456";
        var audience = "api://motorcyclerag-api";
        var inMemoryConfig = new Dictionary<string, string?>
        {
            { "AzureAd:TenantId", tenantId },
            { "AzureAd:ClientId", clientId },
            { "Authentication:Audience", audience }
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemoryConfig)
            .Build();

        // Act
        configuration.WithDerivedAzureAdValues();

        // Assert
        configuration["AzureAd:Audience"].Should().Be(audience);
        configuration["Authentication:Audience"].Should().Be(audience);
        configuration["Jwt:ValidAudience"].Should().Be(audience);
    }

    [Fact]
    public void WithDerivedAzureAdValues_MissingTenantId_ThrowsInvalidOperationException() {
        // Arrange
        var configuration = new ConfigurationBuilder().Build();

        // Act
        var act = () => configuration.WithDerivedAzureAdValues();

        // Assert
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*Tenant ID is not configured*");
    }

    [Fact]
    public void WithValidatedAzureAIEndpoints_NullEndpoints_DoesNotThrow() {
        // Arrange
        var configuration = new ConfigurationBuilder().Build();

        // Act
        var act = () => configuration.WithValidatedAzureAIEndpoints();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void WithValidatedAzureAIEndpoints_ValidEndpoints_DoesNotThrow() {
        // Arrange
        var inMemoryConfig = new Dictionary<string, string?>
        {
            { "AzureAI:SearchServiceEndpoint", "https://mysearch.search.windows.net" }
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemoryConfig)
            .Build();

        // Act
        var act = () => configuration.WithValidatedAzureAIEndpoints();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void WithValidatedAzureAIEndpoints_NonHttpsEndpoint_ThrowsInvalidOperationException() {
        // Arrange
        var inMemoryConfig = new Dictionary<string, string?>
        {
            { "AzureAI:FoundryEndpoint", "http://insecure.endpoint" }
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemoryConfig)
            .Build();

        // Act
        var act = () => configuration.WithValidatedAzureAIEndpoints();

        // Assert
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*MUST use HTTPS*");
    }

    [Fact]
    public void WithValidatedAzureAIEndpoints_PlaceholderEndpoint_ThrowsInvalidOperationException() {
        // Arrange
        var inMemoryConfig = new Dictionary<string, string?>
        {
            { "AzureAI:DocumentIntelligenceEndpoint", "https://your-resource-name.cognitiveservices.azure.com" }
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemoryConfig)
            .Build();

        // Act
        var act = () => configuration.WithValidatedAzureAIEndpoints();

        // Assert
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*appears to be a placeholder*");
    }

    [Fact]
    public void WithDerivedBlobStorageValues_MissingAccountEndpoint_DerivesFromDataProtectionBlobUri() {
        // Arrange
        var inMemoryConfig = new Dictionary<string, string?>
        {
            { "DataProtection:BlobUri", "https://mcrragdevst0125c2ea3c.blob.core.windows.net/dataprotection-keys/bff-keys.xml" }
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemoryConfig)
            .Build();

        // Act
        configuration.WithDerivedBlobStorageValues();

        // Assert
        configuration["BlobStorage:AccountEndpoint"].Should().Be("https://mcrragdevst0125c2ea3c.blob.core.windows.net");
    }

    [Fact]
    public void WithDerivedBlobStorageValues_ExistingAccountEndpoint_PreservesConfiguredValue() {
        // Arrange
        var inMemoryConfig = new Dictionary<string, string?>
        {
            { "BlobStorage:AccountEndpoint", "https://configured.blob.core.windows.net" },
            { "DataProtection:BlobUri", "https://derived.blob.core.windows.net/dataprotection-keys/bff-keys.xml" }
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemoryConfig)
            .Build();

        // Act
        configuration.WithDerivedBlobStorageValues();

        // Assert
        configuration["BlobStorage:AccountEndpoint"].Should().Be("https://configured.blob.core.windows.net");
    }
}
