using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Hosting;
using MotorcycleRAG.API.Configuration;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
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
    public void AddAzureAppConfigurationWithKeyVault_InDevelopmentWithoutRemoteConfiguration_DisablesAppConfiguration() {
        // Arrange
        var builder = CreateBuilder(Environments.Development);

        // Act
        builder.AddAzureAppConfigurationWithKeyVault();

        // Assert
        builder.Configuration[AppConfigurationExtensions.AppConfigurationEnabledKey].Should().Be(bool.FalseString);
    }

    [Fact]
    public void RegisterAzureAppConfiguration_DefaultDelegate_AddsSourcesWithoutLoadingAzure() {
        // Arrange
        var registration = AppConfigurationExtensions.RegisterAzureAppConfiguration;
        var credential = new SequenceTokenCredential((Exception?)null);
        var connectionStringConfiguration = new ConfigurationBuilder();
        var endpointConfiguration = new ConfigurationBuilder();

        // Act
        registration(
            connectionStringConfiguration,
            "Endpoint=https://unit-test.azconfig.io;Id=test;Secret=test",
            null,
            credential,
            Environments.Production);
        registration(
            endpointConfiguration,
            null,
            "https://unit-test.azconfig.io",
            credential,
            Environments.Production);

        // Assert
        connectionStringConfiguration.Sources.Should().ContainSingle();
        endpointConfiguration.Sources.Should().ContainSingle();
    }

    [Fact]
    public void ConfigureAzureAppConfigurationOptions_ConfiguresConnectionStringAndEndpointSources() {
        // Arrange
        var credential = new SequenceTokenCredential((Exception?)null);
        var connectionStringOptions = new AzureAppConfigurationOptions();
        var endpointOptions = new AzureAppConfigurationOptions();

        // Act
        AppConfigurationExtensions.ConfigureAzureAppConfigurationOptions(
            connectionStringOptions,
            "Endpoint=https://unit-test.azconfig.io;Id=test;Secret=test",
            null,
            credential,
            Environments.Production);
        AppConfigurationExtensions.ConfigureAzureAppConfigurationOptions(
            endpointOptions,
            null,
            "https://unit-test.azconfig.io",
            credential,
            Environments.Production);

        // Assert
        GetSelectorCount(connectionStringOptions).Should().Be(3);
        GetSelectorCount(endpointOptions).Should().Be(3);
    }

    [Fact]
    public void AddAzureAppConfigurationWithKeyVault_InProductionWithoutRemoteConfiguration_DerivesLocalValues() {
        // Arrange
        var builder = CreateBuilder(Environments.Production);

        // Act
        builder.AddAzureAppConfigurationWithKeyVault();

        // Assert
        builder.Configuration[AppConfigurationExtensions.AppConfigurationEnabledKey].Should().BeNull();
        builder.Configuration["Jwt:ValidAudience"].Should().Be("client-456");
    }

    [Fact]
    public void AddAzureAppConfigurationWithKeyVault_InProductionWithConnectionString_RegistersWithoutLoadingAzure() {
        // Arrange
        var builder = CreateBuilder(
            Environments.Production,
            new Dictionary<string, string?> {
                ["AppConfig:ConnectionString"] = "Endpoint=https://unit-test.azconfig.io;Id=test;Secret=test"
            });
        var originalRegistration = AppConfigurationExtensions.RegisterAzureAppConfiguration;
        (string? ConnectionString, string? Endpoint, string EnvironmentName) registration = default;
        AppConfigurationExtensions.RegisterAzureAppConfiguration = (_, connectionString, endpoint, _, environmentName) =>
            registration = (connectionString, endpoint, environmentName);

        try {
            // Act
            builder.AddAzureAppConfigurationWithKeyVault();

            // Assert
            registration.ConnectionString.Should().Be("Endpoint=https://unit-test.azconfig.io;Id=test;Secret=test");
            registration.Endpoint.Should().BeNull();
            registration.EnvironmentName.Should().Be(Environments.Production);
            builder.Configuration[AppConfigurationExtensions.AppConfigurationEnabledKey].Should().Be(bool.TrueString);
        }
        finally {
            AppConfigurationExtensions.RegisterAzureAppConfiguration = originalRegistration;
        }
    }

    [Fact]
    public void AddAzureAppConfigurationWithKeyVault_InProductionWithEndpoint_PrewarmsAndChecksConnectivityWithoutLoadingAzure() {
        // Arrange
        var builder = CreateBuilder(
            Environments.Production,
            new Dictionary<string, string?> {
                ["AppConfig:Endpoint"] = "https://unit-test.azconfig.io"
            });
        var originalRegistration = AppConfigurationExtensions.RegisterAzureAppConfiguration;
        var originalPreWarm = AppConfigurationExtensions.PreWarmManagedIdentityTokenAsyncForStartup;
        var originalConnectivity = AppConfigurationExtensions.EnsureTcpConnectivityAsyncForStartup;
        var preWarmCalls = 0;
        string? connectedEndpoint = null;
        string? registeredEndpoint = null;
        AppConfigurationExtensions.PreWarmManagedIdentityTokenAsyncForStartup = _ => {
            preWarmCalls++;
            return Task.CompletedTask;
        };
        AppConfigurationExtensions.EnsureTcpConnectivityAsyncForStartup = endpoint => {
            connectedEndpoint = endpoint;
            return Task.CompletedTask;
        };
        AppConfigurationExtensions.RegisterAzureAppConfiguration = (_, _, endpoint, _, _) => registeredEndpoint = endpoint;

        try {
            // Act
            builder.AddAzureAppConfigurationWithKeyVault();

            // Assert
            preWarmCalls.Should().Be(1);
            connectedEndpoint.Should().Be("https://unit-test.azconfig.io");
            registeredEndpoint.Should().Be("https://unit-test.azconfig.io");
            builder.Configuration[AppConfigurationExtensions.AppConfigurationEnabledKey].Should().Be(bool.TrueString);
        }
        finally {
            AppConfigurationExtensions.RegisterAzureAppConfiguration = originalRegistration;
            AppConfigurationExtensions.PreWarmManagedIdentityTokenAsyncForStartup = originalPreWarm;
            AppConfigurationExtensions.EnsureTcpConnectivityAsyncForStartup = originalConnectivity;
        }
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
    public void WithDerivedAzureAdValues_AzureAdAudience_PreservesConfiguredAudience() {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                ["AzureAd:TenantId"] = "tenant-123",
                ["AzureAd:ClientId"] = "client-456",
                ["AzureAd:Audience"] = "api://motorcyclerag-api"
            })
            .Build();

        // Act
        configuration.WithDerivedAzureAdValues();

        // Assert
        configuration["Authentication:Audience"].Should().Be("api://motorcyclerag-api");
    }

    [Fact]
    public void WithDerivedAzureAdValues_MissingClientId_DoesNotSetAudienceValues() {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                ["AzureAd:TenantId"] = "tenant-123"
            })
            .Build();

        // Act
        configuration.WithDerivedAzureAdValues();

        // Assert
        configuration["Jwt:ValidAudience"].Should().BeNull();
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
    public void WithValidatedAzureAIEndpoints_MalformedHttpsEndpoint_ThrowsInvalidOperationException() {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                ["AzureAI:SearchServiceEndpoint"] = "https://"
            })
            .Build();

        // Act
        var act = () => configuration.WithValidatedAzureAIEndpoints();

        // Assert
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*not a valid HTTPS URL*");
    }

    [Theory]
    [InlineData("https://example.search.windows.net")]
    [InlineData("https://placeholder.search.windows.net")]
    public void WithValidatedAzureAIEndpoints_OtherPlaceholderEndpoint_ThrowsInvalidOperationException(string endpoint) {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                ["AzureAI:SearchServiceEndpoint"] = endpoint
            })
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

    [Fact]
    public async Task PreWarmManagedIdentityTokenAsync_AfterTransientFailure_RetriesAndSucceeds() {
        // Arrange
        var credential = new SequenceTokenCredential(new InvalidOperationException("IMDS unavailable"), null);

        // Act
        await AppConfigurationExtensions.PreWarmManagedIdentityTokenAsync(
            credential,
            maxAttempts: 2,
            retryDelay: TimeSpan.Zero);

        // Assert
        credential.RequestCount.Should().Be(2);
    }

    [Fact]
    public async Task PreWarmManagedIdentityTokenAsyncForStartup_WhenCredentialSucceeds_UsesDefaultStartupSettings() {
        // Arrange
        var credential = new SequenceTokenCredential((Exception?)null);

        // Act
        await AppConfigurationExtensions.PreWarmManagedIdentityTokenAsyncForStartup(credential);

        // Assert
        credential.RequestCount.Should().Be(1);
    }

    [Fact]
    public async Task PreWarmManagedIdentityTokenAsync_OnFinalFailure_PropagatesCredentialException() {
        // Arrange
        var credential = new SequenceTokenCredential(new InvalidOperationException("IMDS unavailable"));

        // Act
        Func<Task> act = () => AppConfigurationExtensions.PreWarmManagedIdentityTokenAsync(
            credential,
            maxAttempts: 1,
            retryDelay: TimeSpan.Zero);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("IMDS unavailable");
    }

    [Fact]
    public async Task EnsureTcpConnectivityAsyncForStartup_WhenConnectionSucceeds_UsesDefaultStartupSettings() {
        // Arrange
        var probe = AppConfigurationExtensions.EnsureTcpConnectivityAsyncForStartup;
        var originalConnector = AppConfigurationExtensions.ConnectToAppConfigurationAsyncForStartup;
        var calls = 0;
        AppConfigurationExtensions.ConnectToAppConfigurationAsyncForStartup = (_, _) => {
            calls++;
            return Task.CompletedTask;
        };

        try {
            // Act
            await probe("https://unit-test.azconfig.io");

            // Assert
            calls.Should().Be(1);
        }
        finally {
            AppConfigurationExtensions.ConnectToAppConfigurationAsyncForStartup = originalConnector;
        }
    }

    [Fact]
    public async Task EnsureTcpConnectivityAsync_WhenFirstAttemptTimesOut_RetriesAndSucceeds() {
        // Arrange
        var calls = 0;
        var incompleteConnection = new TaskCompletionSource();

        // Act
        await AppConfigurationExtensions.EnsureTcpConnectivityAsync(
            "https://unit-test.azconfig.io",
            (_, _) => ++calls == 1 ? incompleteConnection.Task : Task.CompletedTask,
            TimeProvider.System,
            maxAttempts: 2,
            connectTimeout: TimeSpan.Zero,
            retryDelay: TimeSpan.Zero);

        // Assert
        calls.Should().Be(2);
    }

    [Fact]
    public async Task EnsureTcpConnectivityAsync_WhenAllAttemptsTimeOut_ReturnsAfterRetryExhaustion() {
        // Arrange
        var calls = 0;
        var incompleteConnection = new TaskCompletionSource();

        // Act
        var act = () => AppConfigurationExtensions.EnsureTcpConnectivityAsync(
            "https://unit-test.azconfig.io",
            (_, _) => {
                calls++;
                return incompleteConnection.Task;
            },
            TimeProvider.System,
            maxAttempts: 2,
            connectTimeout: TimeSpan.Zero,
            retryDelay: TimeSpan.Zero);

        // Assert
        await act.Should().NotThrowAsync();
        calls.Should().Be(2);
    }

    [Fact]
    public async Task EnsureTcpConnectivityAsync_WhenFinalAttemptFails_PropagatesOriginalException() {
        // Arrange
        var failure = new InvalidOperationException("connection refused");

        // Act
        Func<Task> act = () => AppConfigurationExtensions.EnsureTcpConnectivityAsync(
            "https://unit-test.azconfig.io",
            (_, _) => Task.FromException(failure),
            TimeProvider.System,
            maxAttempts: 2,
            connectTimeout: TimeSpan.Zero,
            retryDelay: TimeSpan.Zero);

        // Assert
        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Should().BeSameAs(failure);
    }

    [Fact]
    public async Task ConnectToAppConfigurationAsyncForStartup_WhenListenerIsAvailable_ConnectsLocally() {
        // Arrange
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var acceptTask = listener.AcceptTcpClientAsync();

        // Act
        await AppConfigurationExtensions.ConnectToAppConfigurationAsyncForStartup("127.0.0.1", endpoint.Port);
        using var acceptedClient = await acceptTask;

        // Assert
        acceptedClient.Connected.Should().BeTrue();
    }

    [Fact]
    public void WithDerivedBlobStorageValues_MissingBlobUri_DoesNotSetAccountEndpoint() {
        AssertBlobUriDoesNotDeriveAccountEndpoint(null);
    }

    [Fact]
    public void WithDerivedBlobStorageValues_InvalidBlobUri_DoesNotSetAccountEndpoint() {
        AssertBlobUriDoesNotDeriveAccountEndpoint("not-a-uri");
    }

    private static void AssertBlobUriDoesNotDeriveAccountEndpoint(string? blobUri) {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                ["DataProtection:BlobUri"] = blobUri
            })
            .Build();

        // Act
        configuration.WithDerivedBlobStorageValues();

        // Assert
        configuration["BlobStorage:AccountEndpoint"].Should().BeNull();
    }

    private static WebApplicationBuilder CreateBuilder(string environmentName, Dictionary<string, string?>? additionalConfiguration = null) {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions {
            EnvironmentName = environmentName
        });
        var configuration = new Dictionary<string, string?> {
            ["AzureAd:TenantId"] = "tenant-123",
            ["AzureAd:ClientId"] = "client-456"
        };

        if (additionalConfiguration is not null) {
            foreach (var item in additionalConfiguration) {
                configuration[item.Key] = item.Value;
            }
        }

        builder.Configuration.AddInMemoryCollection(configuration);
        return builder;
    }

    private static int GetSelectorCount(AzureAppConfigurationOptions options) {
        var selectors = (System.Collections.IEnumerable)options.GetType()
            .GetProperty("Selectors", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(options)!;
        return selectors.Cast<object>().Count();
    }

    private sealed class SequenceTokenCredential(params Exception?[] outcomes) : TokenCredential {
        private readonly Queue<Exception?> _outcomes = new(outcomes);

        public int RequestCount { get; private set; }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => GetNextToken();

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetNextToken());

        private AccessToken GetNextToken() {
            RequestCount++;
            var outcome = _outcomes.Dequeue();
            if (outcome is not null) {
                throw outcome;
            }

            return new AccessToken("unit-test-token", DateTimeOffset.MaxValue);
        }
    }
}
