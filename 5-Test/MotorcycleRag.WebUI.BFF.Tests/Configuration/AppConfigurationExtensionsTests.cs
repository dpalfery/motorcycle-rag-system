using Azure.Core;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Hosting;
using MotorcycleRag.WebUI.BFF.Configuration;

namespace MotorcycleRag.WebUI.BFF.Tests.Configuration;

public class AppConfigurationExtensionsTests {
    [Fact]
    public void AddBffAzureAppConfiguration_PublicWrapperInDevelopmentWithoutSettings_DoesNotRegisterRemoteConfiguration() {
        // Arrange
        var builder = CreateBuilder(Environments.Development);
        var originalServiceCount = builder.Services.Count;

        // Act
        var result = builder.AddBffAzureAppConfiguration();

        // Assert
        result.Should().BeSameAs(builder);
        builder.Services.Should().HaveCount(originalServiceCount);
    }

    [Fact]
    public void AddBffAzureAppConfiguration_InDevelopment_SkipsAllRemoteConfigurationWork() {
        // Arrange
        var builder = CreateBuilder(Environments.Development, ("AppConfig:Endpoint", "https://unit-test.azconfig.io"));
        var originalServiceCount = builder.Services.Count;
        var credentialCalls = 0;
        var preWarmCalls = 0;
        var connectivityCalls = 0;
        var registrationCalls = 0;

        // Act
        var result = AppConfigurationExtensions.AddBffAzureAppConfiguration(
            builder,
            optional: false,
            skipRemoteConfigurationLoad: false,
            createCredential: () => {
                credentialCalls++;
                return CreateCredential();
            },
            preWarmManagedIdentityTokenAsync: _ => {
                preWarmCalls++;
                return Task.CompletedTask;
            },
            ensureTcpConnectivityAsync: _ => {
                connectivityCalls++;
                return Task.CompletedTask;
            },
            registerAzureAppConfiguration: (_, _, _, _, _, _) => registrationCalls++);

        // Assert
        result.Should().BeSameAs(builder);
        (credentialCalls, preWarmCalls, connectivityCalls, registrationCalls).Should().Be((0, 0, 0, 0));
        builder.Services.Should().HaveCount(originalServiceCount);
    }

    [Fact]
    public void AddBffAzureAppConfiguration_WithoutSettings_SkipsAllRemoteConfigurationWork() {
        // Arrange
        var builder = CreateBuilder(Environments.Production);
        var originalServiceCount = builder.Services.Count;
        var credentialCalls = 0;
        var registrationCalls = 0;

        // Act
        var result = AppConfigurationExtensions.AddBffAzureAppConfiguration(
            builder,
            optional: false,
            skipRemoteConfigurationLoad: false,
            createCredential: () => {
                credentialCalls++;
                return CreateCredential();
            },
            preWarmManagedIdentityTokenAsync: _ => throw new InvalidOperationException("Prewarm must not run."),
            ensureTcpConnectivityAsync: _ => throw new InvalidOperationException("Connectivity check must not run."),
            registerAzureAppConfiguration: (_, _, _, _, _, _) => registrationCalls++);

        // Assert
        result.Should().BeSameAs(builder);
        credentialCalls.Should().Be(0);
        registrationCalls.Should().Be(0);
        builder.Services.Should().HaveCount(originalServiceCount);
    }

    [Fact]
    public void AddBffAzureAppConfiguration_WithConnectionString_RegistersConnectionStringWithoutEndpointStartupCalls() {
        // Arrange
        const string connectionString = "Endpoint=https://unit-test.azconfig.io;Id=test;Secret=test";
        var builder = CreateBuilder(Environments.Production, ("AppConfig:ConnectionString", connectionString));
        var originalServiceCount = builder.Services.Count;
        var credential = CreateCredential();
        var credentialCalls = 0;
        RegistrationCall? registration = null;
        AppConfigurationExtensions.AzureAppConfigurationRegistrationIntent? intent = null;

        // Act
        var result = AppConfigurationExtensions.AddBffAzureAppConfiguration(
            builder,
            optional: false,
            skipRemoteConfigurationLoad: false,
            createCredential: () => {
                credentialCalls++;
                return credential;
            },
            preWarmManagedIdentityTokenAsync: _ => throw new InvalidOperationException("Connection-string startup must not prewarm."),
            ensureTcpConnectivityAsync: _ => throw new InvalidOperationException("Connection-string startup must not probe TCP."),
            registerAzureAppConfiguration: (configuration, configuredConnectionString, endpoint, configuredCredential, environmentName, optional) =>
                registration = new RegistrationCall(configuration, configuredConnectionString, endpoint, configuredCredential, environmentName, optional),
            observeRegistrationIntent: observed => intent = observed);

        // Assert
        result.Should().BeSameAs(builder);
        credentialCalls.Should().Be(1);
        registration.Should().Be(new RegistrationCall(builder.Configuration, connectionString, null, credential, Environments.Production, false));
        intent.Should().NotBeNull();
        intent!.ConnectionString.Should().Be(connectionString);
        intent.Endpoint.Should().BeNull();
        intent.Optional.Should().BeFalse();
        intent.ComponentLabel.Should().Be("bff");
        intent.EnvironmentLabel.Should().Be(Environments.Production);
        intent.KeyVaultCredential.Should().BeSameAs(credential);
        intent.SentinelKey.Should().Be("Settings:Sentinel");
        intent.RefreshAll.Should().BeTrue();
        intent.RefreshInterval.Should().Be(TimeSpan.FromSeconds(30));
        intent.UsesConnectionString.Should().BeTrue();
        builder.Services.Should().HaveCountGreaterThan(originalServiceCount);
    }

    [Fact]
    public void AddBffAzureAppConfiguration_WithEndpoint_PrewarmsChecksConnectivityAndForwardsEnvironmentLabel() {
        // Arrange
        const string endpoint = "https://unit-test.azconfig.io";
        var builder = CreateBuilder(Environments.Staging, ("AppConfig:Endpoint", endpoint));
        var credential = CreateCredential();
        var calls = new List<string>();
        RegistrationCall? registration = null;
        AppConfigurationExtensions.AzureAppConfigurationRegistrationIntent? intent = null;

        // Act
        AppConfigurationExtensions.AddBffAzureAppConfiguration(
            builder,
            optional: false,
            skipRemoteConfigurationLoad: false,
            createCredential: () => {
                calls.Add("credential");
                return credential;
            },
            preWarmManagedIdentityTokenAsync: configuredCredential => {
                configuredCredential.Should().BeSameAs(credential);
                calls.Add("prewarm");
                return Task.CompletedTask;
            },
            ensureTcpConnectivityAsync: configuredEndpoint => {
                configuredEndpoint.Should().Be(endpoint);
                calls.Add("connectivity");
                return Task.CompletedTask;
            },
            registerAzureAppConfiguration: (configuration, connectionString, configuredEndpoint, configuredCredential, environmentName, optional) => {
                calls.Add("register");
                registration = new RegistrationCall(configuration, connectionString, configuredEndpoint, configuredCredential, environmentName, optional);
            },
            observeRegistrationIntent: observed => intent = observed);

        // Assert
        calls.Should().Equal("credential", "prewarm", "connectivity", "register");
        registration.Should().Be(new RegistrationCall(builder.Configuration, null, endpoint, credential, Environments.Staging, false));
        intent.Should().Be(new AppConfigurationExtensions.AzureAppConfigurationRegistrationIntent(
            null,
            endpoint,
            false,
            "bff",
            Environments.Staging,
            credential,
            "Settings:Sentinel",
            true,
            TimeSpan.FromSeconds(30)));
        intent!.UsesConnectionString.Should().BeFalse();
    }

    [Fact]
    public void AddBffAzureAppConfiguration_WithOptionalRegistration_ForwardsOptionalFlag() {
        // Arrange
        var builder = CreateBuilder(Environments.Production, ("AppConfig:ConnectionString", "Endpoint=https://unit-test.azconfig.io;Id=test;Secret=test"));
        var credential = CreateCredential();
        bool? optional = null;
        AppConfigurationExtensions.AzureAppConfigurationRegistrationIntent? intent = null;

        // Act
        AppConfigurationExtensions.AddBffAzureAppConfiguration(
            builder,
            optional: true,
            skipRemoteConfigurationLoad: false,
            createCredential: () => credential,
            preWarmManagedIdentityTokenAsync: _ => throw new InvalidOperationException("Prewarm must not run."),
            ensureTcpConnectivityAsync: _ => throw new InvalidOperationException("Connectivity check must not run."),
            registerAzureAppConfiguration: (_, _, _, _, _, forwardedOptional) => optional = forwardedOptional,
            observeRegistrationIntent: observed => intent = observed);

        // Assert
        optional.Should().BeTrue();
        intent!.Optional.Should().BeTrue();
    }

    [Fact]
    public void AddBffAzureAppConfiguration_WhenRemoteLoadIsSkipped_DoesNotInvokeRegistrationOrEndpointStartupCalls() {
        // Arrange
        var builder = CreateBuilder(Environments.Production, ("AppConfig:Endpoint", "https://unit-test.azconfig.io"));
        var originalServiceCount = builder.Services.Count;
        var registrationCalls = 0;

        // Act
        AppConfigurationExtensions.AddBffAzureAppConfiguration(
            builder,
            optional: false,
            skipRemoteConfigurationLoad: true,
            createCredential: CreateCredential,
            preWarmManagedIdentityTokenAsync: _ => throw new InvalidOperationException("Prewarm must not run."),
            ensureTcpConnectivityAsync: _ => throw new InvalidOperationException("Connectivity check must not run."),
            registerAzureAppConfiguration: (_, _, _, _, _, _) => registrationCalls++);

        // Assert
        registrationCalls.Should().Be(0);
        builder.Services.Should().HaveCountGreaterThan(originalServiceCount);
    }

    [Fact]
    public void AddBffAzureAppConfiguration_PublicWrapperWithConnectionStringAndSkippedLoad_RegistersRefreshServices() {
        // Arrange
        var builder = CreateBuilder(
            Environments.Production,
            ("AppConfig:ConnectionString", "Endpoint=https://unit-test.azconfig.io;Id=test;Secret=test"));
        var originalServiceCount = builder.Services.Count;

        // Act
        var result = builder.AddBffAzureAppConfiguration(skipRemoteConfigurationLoad: true);

        // Assert
        result.Should().BeSameAs(builder);
        builder.Services.Should().HaveCountGreaterThan(originalServiceCount);
    }

    [Fact]
    public void RegisterAzureAppConfiguration_WithConnectionString_AddsSourceWithoutBuildingConfiguration() {
        // Arrange
        var configuration = new ConfigurationBuilder();
        var originalSourceCount = configuration.Sources.Count;

        // Act
        AppConfigurationExtensions.RegisterAzureAppConfiguration(
            configuration,
            "Endpoint=https://unit-test.azconfig.io;Id=test;Secret=test",
            appConfigEndpoint: null,
            CreateCredential(),
            Environments.Production,
            optional: true);

        // Assert
        configuration.Sources.Should().HaveCount(originalSourceCount + 1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConfigureAzureAppConfigurationOptions_WithEitherConnectionMode_CreatesAConfigurationRefresher(bool usesConnectionString) {
        // Arrange
        var options = new AzureAppConfigurationOptions();
        var credential = CreateCredential();

        // Act
        AppConfigurationExtensions.ConfigureAzureAppConfigurationOptions(
            options,
            usesConnectionString ? "Endpoint=https://unit-test.azconfig.io;Id=test;Secret=test" : null,
            usesConnectionString ? null : "https://unit-test.azconfig.io",
            credential,
            Environments.Production);

        // Assert
        options.GetRefresher().Should().NotBeNull();
    }

    [Fact]
    public async Task ConnectToAppConfigurationAsync_WithAlreadyCancelledToken_ThrowsWithoutOpeningConnection() {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        var act = () => AppConfigurationExtensions.ConnectToAppConfigurationAsync(
            "unit-test.azconfig.io",
            443,
            cancellation.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task EnsureTcpConnectivityAsync_WhenLoopbackListenerIsReady_ConnectsUsingDefaultConnector() {
        // Arrange
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var listenerEndpoint = (IPEndPoint)listener.LocalEndpoint;
        using var acceptanceCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var acceptedClientTask = listener.AcceptTcpClientAsync(acceptanceCancellation.Token);

        // Act
        await AppConfigurationExtensions.EnsureTcpConnectivityAsync(
            "https://127.0.0.1",
            maxAttempts: 1,
            connectTimeoutMs: 1000,
            retryDelayMs: 0,
            port: listenerEndpoint.Port);
        using var acceptedClient = await acceptedClientTask;

        // Assert
        acceptedClient.Client.LocalEndPoint.Should().Be(listenerEndpoint);
        ((IPEndPoint)acceptedClient.Client.RemoteEndPoint!).Address.Should().Be(IPAddress.Loopback);
    }

    [Fact]
    public async Task PreWarmManagedIdentityTokenAsync_WhenCredentialSucceeds_RequestsTokenOnce() {
        // Arrange
        var credential = new SequenceTokenCredential((Exception?)null);

        // Act
        await AppConfigurationExtensions.PreWarmManagedIdentityTokenAsync(credential, maxAttempts: 3, retryDelayMs: 0);

        // Assert
        credential.RequestCount.Should().Be(1);
    }

    [Fact]
    public async Task PreWarmManagedIdentityTokenAsync_WhenTransientFailureThenSuccess_RetriesUntilTokenIsAvailable() {
        // Arrange
        var credential = new SequenceTokenCredential(new InvalidOperationException("IMDS unavailable"), null);

        // Act
        await AppConfigurationExtensions.PreWarmManagedIdentityTokenAsync(credential, maxAttempts: 2, retryDelayMs: 0);

        // Assert
        credential.RequestCount.Should().Be(2);
    }

    [Fact]
    public async Task PreWarmManagedIdentityTokenAsync_WhenAttemptsAreExhausted_PropagatesTerminalFailure() {
        // Arrange
        var credential = new SequenceTokenCredential(
            new InvalidOperationException("IMDS unavailable"),
            new InvalidOperationException("IMDS unavailable"));

        // Act
        var act = () => AppConfigurationExtensions.PreWarmManagedIdentityTokenAsync(credential, maxAttempts: 2, retryDelayMs: 0);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("IMDS unavailable");
        credential.RequestCount.Should().Be(2);
    }

    [Fact]
    public async Task EnsureTcpConnectivityWithRetriesAsync_WhenConnectionSucceeds_UsesConfiguredHostPortAndSingleAttempt() {
        // Arrange
        var calls = new List<(string Host, int Port, bool CanBeCanceled)>();

        // Act
        await AppConfigurationExtensions.EnsureTcpConnectivityWithRetriesAsync(
            "https://unit-test.azconfig.io",
            (host, port, cancellationToken) => {
                calls.Add((host, port, cancellationToken.CanBeCanceled));
                return Task.CompletedTask;
            },
            TimeProvider.System,
            maxAttempts: 3,
            connectTimeout: TimeSpan.FromSeconds(1),
            retryDelay: TimeSpan.Zero,
            port: 8443);

        // Assert
        calls.Should().Equal(("unit-test.azconfig.io", 8443, true));
    }

    [Fact]
    public async Task EnsureTcpConnectivityWithRetriesAsync_WhenAttemptsTimeout_ExhaustsRetriesWithoutThrowing() {
        // Arrange
        var calls = 0;

        // Act
        await AppConfigurationExtensions.EnsureTcpConnectivityWithRetriesAsync(
            "https://unit-test.azconfig.io",
            (_, _, _) => {
                calls++;
                throw new OperationCanceledException("Timed out.");
            },
            TimeProvider.System,
            maxAttempts: 3,
            connectTimeout: TimeSpan.Zero,
            retryDelay: TimeSpan.Zero);

        // Assert
        calls.Should().Be(3);
    }

    [Fact]
    public async Task EnsureTcpConnectivityWithRetriesAsync_WhenFinalNonTimeoutFailureOccurs_PropagatesFailureAfterRetries() {
        // Arrange
        var calls = 0;

        // Act
        var act = () => AppConfigurationExtensions.EnsureTcpConnectivityWithRetriesAsync(
            "https://unit-test.azconfig.io",
            (_, _, _) => {
                calls++;
                throw new InvalidOperationException("TCP probe failed.");
            },
            TimeProvider.System,
            maxAttempts: 3,
            connectTimeout: TimeSpan.Zero,
            retryDelay: TimeSpan.Zero);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("TCP probe failed.");
        calls.Should().Be(3);
    }

    private static WebApplicationBuilder CreateBuilder(string environmentName, params (string Key, string Value)[] settings) {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions {
            EnvironmentName = environmentName
        });

        foreach (var (key, value) in settings) {
            builder.Configuration[key] = value;
        }

        return builder;
    }

    private static TokenCredential CreateCredential() => new SequenceTokenCredential((Exception?)null);

    private sealed record RegistrationCall(
        IConfigurationBuilder Configuration,
        string? ConnectionString,
        string? Endpoint,
        TokenCredential Credential,
        string EnvironmentName,
        bool Optional);

    private sealed class SequenceTokenCredential(params Exception?[] outcomes) : TokenCredential {
        private readonly Queue<Exception?> _outcomes = new(outcomes);

        public int RequestCount { get; private set; }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => GetAccessToken();

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetAccessToken());

        private AccessToken GetAccessToken() {
            RequestCount++;
            var outcome = _outcomes.Count > 0 ? _outcomes.Dequeue() : null;
            if (outcome is not null) {
                throw outcome;
            }

            return new AccessToken("unit-test-token", DateTimeOffset.UtcNow.AddHours(1));
        }
    }
}
