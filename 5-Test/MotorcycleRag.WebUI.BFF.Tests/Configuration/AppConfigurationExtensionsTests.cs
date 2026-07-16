using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Azure.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using MotorcycleRag.WebUI.BFF.Configuration;

namespace MotorcycleRag.WebUI.BFF.Tests.Configuration;

public class AppConfigurationExtensionsTests {
    [Fact]
    public void AddBffAzureAppConfiguration_InDevelopmentWithoutAzureSettings_ReturnsBuilder() {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions {
            EnvironmentName = Environments.Development
        });

        var result = builder.AddBffAzureAppConfiguration();

        result.Should().BeSameAs(builder);
    }

    [Theory]
    [InlineData("AppConfig:ConnectionString", "Endpoint=https://config.example;Id=test;Secret=secret")]
    [InlineData("AppConfig:Endpoint", "https://config.example")]
    public void AddBffAzureAppConfiguration_InDevelopmentWithAzureSetting_SkipsRemoteConfiguration(
        string key,
        string value) {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions {
            EnvironmentName = Environments.Development
        });
        builder.Configuration[key] = value;
        var originalProviderCount = ((IConfigurationRoot)builder.Configuration).Providers.Count();

        var result = builder.AddBffAzureAppConfiguration();

        result.Should().BeSameAs(builder);
        ((IConfigurationRoot)builder.Configuration).Providers.Should().HaveCount(originalProviderCount);
    }

    [Fact]
    public void AddBffAzureAppConfiguration_InProductionWithoutAzureSettings_ReturnsBuilder() {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions {
            EnvironmentName = Environments.Production
        });
        builder.Configuration["AppConfig:ConnectionString"] = null;
        builder.Configuration["AppConfig:Endpoint"] = null;

        var result = builder.AddBffAzureAppConfiguration();

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void AddBffAzureAppConfiguration_InStagingWithoutAzureSettings_ReturnsBuilder() {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions {
            EnvironmentName = Environments.Staging
        });
        builder.Configuration["AppConfig:ConnectionString"] = "";
        builder.Configuration["AppConfig:Endpoint"] = "";

        var result = builder.AddBffAzureAppConfiguration();

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void AddBffAzureAppConfiguration_InProductionWithConnectionString_RegistersAzureAppConfiguration() {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions {
            EnvironmentName = Environments.Production
        });
        builder.Configuration["AppConfig:ConnectionString"] =
            "Endpoint=https://motorcycle-rag-config.azconfig.io;Id=test;Secret=dGVzdA==";
        builder.Configuration["AppConfig:Endpoint"] = null;

        // skipRemoteConfigurationLoad avoids Azure App Configuration's ~100s startup timeout.
        var result = builder.AddBffAzureAppConfiguration(skipRemoteConfigurationLoad: true);

        result.Should().BeSameAs(builder);
        builder.Services.Should().Contain(descriptor =>
            descriptor.ServiceType.FullName!.Contains("IConfigurationRefresher", StringComparison.Ordinal)
            || descriptor.ServiceType.Name.Contains("AzureAppConfiguration", StringComparison.Ordinal)
            || descriptor.ServiceType.FullName!.Contains("AzureAppConfiguration", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PreWarmManagedIdentityTokenAsync_WhenTokenSucceeds_Completes() {
        var method = GetPrivateMethod("PreWarmManagedIdentityTokenAsync");

        var credential = new Mock<TokenCredential>();
        credential
            .Setup(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccessToken("token", DateTimeOffset.UtcNow.AddHours(1)));

        await ((Task)method.Invoke(null, [credential.Object, 10, 1])!);

        credential.Verify(
            c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PreWarmManagedIdentityTokenAsync_WhenCredentialFailsOnceThenSucceeds_Retries() {
        var method = GetPrivateMethod("PreWarmManagedIdentityTokenAsync");

        var credential = new Mock<TokenCredential>();
        credential
            .SetupSequence(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("IMDS not ready"))
            .ReturnsAsync(new AccessToken("token", DateTimeOffset.UtcNow.AddHours(1)));

        await ((Task)method.Invoke(null, [credential.Object, 10, 1])!);

        credential.Verify(
            c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task EnsureTcpConnectivityAsync_WhenHostAcceptsConnection_Completes() {
        var method = GetPrivateMethod("EnsureTcpConnectivityAsync");

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var acceptCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var acceptTask = listener.AcceptTcpClientAsync(acceptCts.Token);

        await ((Task)method.Invoke(null, ["https://127.0.0.1", 2, 500, 1, port])!);

        using var client = await acceptTask;
        client.Connected.Should().BeTrue();
    }

    [Fact]
    public async Task EnsureTcpConnectivityAsync_WhenHostIsUnreachable_ExhaustsRetries() {
        var method = GetPrivateMethod("EnsureTcpConnectivityAsync");

        // Closed loopback port fails immediately (no external network / blackhole waits).
        var closedPort = GetClosedLoopbackPort();

        var act = async () => {
            var task = (Task)method.Invoke(null, ["https://127.0.0.1", 3, 100, 1, closedPort])!;
            await task;
        };

        await act.Should().ThrowAsync<SocketException>();
    }

    [Fact]
    public async Task EnsureTcpConnectivityAsync_WhenConnectionTimesOut_RetriesBeforeGivingUp() {
        var method = GetPrivateMethod("EnsureTcpConnectivityAsync");

        // Closed port: first attempts hit the Exception retry path with 1ms delay; final attempt rethrows.
        var closedPort = GetClosedLoopbackPort();

        var act = async () => {
            var task = (Task)method.Invoke(null, ["https://127.0.0.1", 2, 50, 1, closedPort])!;
            await task;
        };

        await act.Should().ThrowAsync<SocketException>();
    }

    [Fact]
    public void AddBffAzureAppConfiguration_InProductionWithEndpoint_RegistersAzureAppConfiguration() {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions {
            EnvironmentName = Environments.Production
        });
        builder.Configuration["AppConfig:ConnectionString"] = null;
        builder.Configuration["AppConfig:Endpoint"] = "https://motorcycle-rag-config.azconfig.io";

        var result = builder.AddBffAzureAppConfiguration(skipRemoteConfigurationLoad: true);

        result.Should().BeSameAs(builder);
        builder.Services.Should().Contain(descriptor =>
            descriptor.ServiceType.FullName!.Contains("IConfigurationRefresher", StringComparison.Ordinal)
            || descriptor.ServiceType.Name.Contains("AzureAppConfiguration", StringComparison.Ordinal)
            || descriptor.ServiceType.FullName!.Contains("AzureAppConfiguration", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PreWarmManagedIdentityTokenAsync_WhenAllAttemptsFail_ThrowsException() {
        var method = GetPrivateMethod("PreWarmManagedIdentityTokenAsync");

        var credential = new Mock<TokenCredential>();
        credential
            .Setup(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("IMDS failure"));

        var act = async () => {
            await ((Task)method.Invoke(null, [credential.Object, 3, 1])!);
        };

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("IMDS failure");
    }

    [Fact]
    public async Task EnsureTcpConnectivityAsync_WhenAllAttemptsTimeout_CompletesSuccessfully() {
        var method = GetPrivateMethod("EnsureTcpConnectivityAsync");

        var act = async () => {
            var task = (Task)method.Invoke(null, ["https://127.0.0.1", 1, 0, 1, 443])!;
            await task;
        };

        await act.Should().NotThrowAsync();
    }

    private static MethodInfo GetPrivateMethod(string name) {
        var method = typeof(AppConfigurationExtensions).GetMethod(
            name,
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return method!;
    }

    private static int GetClosedLoopbackPort() {
        var temp = new TcpListener(IPAddress.Loopback, 0);
        temp.Start();
        var port = ((IPEndPoint)temp.LocalEndpoint).Port;
        temp.Stop();
        return port;
    }
}
