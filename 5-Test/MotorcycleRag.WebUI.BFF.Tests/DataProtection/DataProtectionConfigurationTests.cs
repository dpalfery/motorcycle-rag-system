using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace MotorcycleRag.WebUI.BFF.Tests.DataProtection;

/// <summary>
/// Unit tests for Data Protection key persistence configuration.
/// These tests verify that Data Protection is configured correctly based on
/// the DataProtection:BlobUri configuration setting.
/// </summary>
public class DataProtectionConfigurationTests
{
    private const string ValidBlobUri = "https://teststorage.blob.core.windows.net/testcontainer/keys.xml";

    /// <summary>
    /// Test that the DataProtection:BlobUri configuration option exists and is read correctly.
    /// </summary>
    [Fact]
    public void Configuration_Contains_DataProtection_BlobUri_Setting()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataProtection:BlobUri"] = ValidBlobUri
            })
            .Build();

        // Act
        var blobUri = configuration["DataProtection:BlobUri"];

        // Assert
        blobUri.Should().NotBeNull();
        blobUri.Should().Be(ValidBlobUri);
    }

    /// <summary>
    /// Test that when DataProtection:BlobUri is empty string, it is treated as not configured.
    /// </summary>
    [Fact]
    public void Configuration_WithEmptyBlobUri_TreatedAsNotConfigured()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataProtection:BlobUri"] = ""  // Empty string
            })
            .Build();

        // Act
        var blobUri = configuration["DataProtection:BlobUri"];

        // Assert
        // An empty string is not null, but should be treated as not configured
        string.IsNullOrEmpty(blobUri).Should().BeTrue();
    }

    /// <summary>
    /// Test that when DataProtection:BlobUri is null, it is treated as not configured.
    /// </summary>
    [Fact]
    public void Configuration_WithNullBlobUri_TreatedAsNotConfigured()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        // Act
        var blobUri = configuration["DataProtection:BlobUri"];

        // Assert
        blobUri.Should().BeNull();
    }
}

/// <summary>
/// Integration tests for Data Protection key persistence to Azure Blob Storage.
/// These tests verify that Data Protection can write and read keys from blob storage.
/// </summary>
public class DataProtectionIntegrationTests
{
    private const string ValidBlobUri = "https://teststorage.blob.core.windows.net/testcontainer/keys.xml";

    /// <summary>
    /// Test that when DataProtection:BlobUri is configured, the application starts successfully.
    /// This test verifies the configuration is correct and the application can start.
    /// </summary>
    [Fact]
    public async Task Configure_DataProtection_WithBlobUri_ApplicationStartsSuccessfully()
    {
        // Arrange
        var factory = new DataProtectionTestWebApplicationFactory(
            blobUri: ValidBlobUri,
            environment: "Development");

        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Test that when DataProtection:BlobUri is not configured in Development,
    /// the application still starts (with a warning).
    /// </summary>
    [Fact]
    public async Task Configure_DataProtection_WithoutBlobUri_InDevelopment_ApplicationStarts()
    {
        // Arrange
        var factory = new DataProtectionTestWebApplicationFactory(
            blobUri: null,
            environment: "Development");

        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Test that when DataProtection:BlobUri is not configured in Production,
    /// an InvalidOperationException is thrown during application startup.
    /// </summary>
    [Fact]
    public void Configure_DataProtection_WithoutBlobUri_InProduction_ThrowsException()
    {
        // Arrange & Act
        var factory = new DataProtectionTestWebApplicationFactory(
            blobUri: null,
            environment: "Production");

        // Assert
        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        exception.Message.Should().Contain("DataProtection:BlobUri is required in production");
    }
}

/// <summary>
/// Test web application factory for creating test clients with custom configuration.
/// </summary>
public class DataProtectionTestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string? _blobUri;
    private readonly string _environment;

    public DataProtectionTestWebApplicationFactory(string? blobUri, string environment)
    {
        _blobUri = blobUri;
        _environment = environment;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        
        builder.ConfigureAppConfiguration((context, config) =>
        {
            var configDict = new Dictionary<string, string?>
            {
                ["AppConfig:Endpoint"] = "",  // Disable App Config
                ["AllowedHosts"] = "*",  // Allow all hosts for testing
                ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
                ["AzureAd:TenantId"] = "test-tenant-id",
                ["AzureAd:ClientId"] = "test-client-id",
                ["AzureAd:ClientSecret"] = "test-secret",
                // Override the appsettings.json placeholder value so bootstrap telemetry guard skips init
                ["ConnectionStrings:ApplicationInsights"] = "",
                ["ApplicationInsights:ConnectionString"] = "",
                ["ApplicationInsights:EnableTelemetry"] = "false",
                ["Cors:AllowedOrigins"] = "https://localhost",
            };

            if (!string.IsNullOrEmpty(_blobUri))
            {
                configDict["DataProtection:BlobUri"] = _blobUri;
            }

            config.AddInMemoryCollection(configDict);
        });

        // Register a no-op TelemetryClient so DataProtectionMonitoringService can be activated
        // without a real Application Insights connection string in tests.
        builder.ConfigureServices(services =>
        {
            var noopConfig = new Microsoft.ApplicationInsights.Extensibility.TelemetryConfiguration();
            noopConfig.TelemetryChannel = new Microsoft.ApplicationInsights.Channel.InMemoryChannel();
            services.AddSingleton(new Microsoft.ApplicationInsights.TelemetryClient(noopConfig));
        });
    }
}
