using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MotorcycleRag.WebUI.BFF.Configuration.Services;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

namespace MotorcycleRag.WebUI.BFF.Tests.Configuration.Services;

public class YarpServiceConfigurationTests {
    [Fact]
    public void AddBffReverseProxy_RegistersReverseProxyServices() {
        var services = CreateServices();
        var configuration = CreateConfiguration();

        var result = services.AddBffReverseProxy(configuration);

        result.Should().BeSameAs(services);
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IProxyConfigProvider)
            || descriptor.ServiceType.Name.Contains("Proxy", StringComparison.Ordinal));
    }

    [Fact]
    public void AddBffReverseProxy_LoadsRoutesAndClustersFromConfiguration() {
        var services = CreateServices();
        var configuration = CreateConfiguration();
        services.AddSingleton<IConfiguration>(configuration);

        services.AddBffReverseProxy(configuration);
        using var provider = services.BuildServiceProvider();

        var configProvider = provider.GetRequiredService<IProxyConfigProvider>();
        var config = configProvider.GetConfig();

        config.Routes.Should().ContainSingle(route => route.ClusterId == "api-cluster");
        config.Clusters.Should().ContainSingle(cluster => cluster.ClusterId == "api-cluster");
    }

    [Fact]
    public async Task AddBffReverseProxy_WhenProxying_SetsConfiguredApiHostHeader() {
        var request = await CaptureProxyRequestAsync(accessToken: null);

        request.Host.Should().Be("motorag.api.palfery.com");
        request.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task AddBffReverseProxy_WhenSessionHasAccessToken_ForwardsBearerToken() {
        var request = await CaptureProxyRequestAsync(accessToken: "session-access-token");

        request.Host.Should().Be("motorag.api.palfery.com");
        request.Authorization!.Scheme.Should().Be("Bearer");
        request.Authorization.Parameter.Should().Be("session-access-token");
    }

    private static ServiceCollection CreateServices() {
        var services = new ServiceCollection();
        services.AddLogging();
        return services;
    }

    private static IConfiguration CreateConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                ["ReverseProxy:Routes:api-route:ClusterId"] = "api-cluster",
                ["ReverseProxy:Routes:api-route:Match:Path"] = "/api/{**catch-all}",
                ["ReverseProxy:Clusters:api-cluster:Destinations:destination1:Address"] = "https://api.example/"
            })
            .Build();

    private static async Task<(string? Host, System.Net.Http.Headers.AuthenticationHeaderValue? Authorization)>
        CaptureProxyRequestAsync(string? accessToken) {
        var authentication = new Mock<IAuthenticationService>();
        var properties = new AuthenticationProperties();
        if (accessToken is not null) {
            properties.StoreTokens([new AuthenticationToken {
                Name = "access_token",
                Value = accessToken
            }]);
        }

        authentication
            .Setup(service => service.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string?>()))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(
                new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity()),
                properties,
                "TestAuthentication")));

        string? capturedHost = null;
        System.Net.Http.Headers.AuthenticationHeaderValue? capturedAuthorization = null;
        async ValueTask<ForwarderError> CaptureAsync(
            HttpContext context,
            string destinationPrefix,
            HttpTransformer transformer,
            CancellationToken cancellationToken) {
            using var proxyRequest = new HttpRequestMessage();
            await transformer.TransformRequestAsync(
                context,
                proxyRequest,
                destinationPrefix,
                cancellationToken);
            capturedHost = proxyRequest.Headers.Host;
            capturedAuthorization = proxyRequest.Headers.Authorization;
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return ForwarderError.None;
        }

        var forwarder = new Mock<IHttpForwarder>();
        forwarder
            .Setup(service => service.SendAsync(
                It.IsAny<HttpContext>(),
                It.IsAny<string>(),
                It.IsAny<HttpMessageInvoker>(),
                It.IsAny<ForwarderRequestConfig>(),
                It.IsAny<HttpTransformer>()))
            .Returns((
                HttpContext context,
                string destinationPrefix,
                HttpMessageInvoker _,
                ForwarderRequestConfig _,
                HttpTransformer transformer) =>
                CaptureAsync(context, destinationPrefix, transformer, CancellationToken.None));
        forwarder
            .Setup(service => service.SendAsync(
                It.IsAny<HttpContext>(),
                It.IsAny<string>(),
                It.IsAny<HttpMessageInvoker>(),
                It.IsAny<ForwarderRequestConfig>(),
                It.IsAny<HttpTransformer>(),
                It.IsAny<CancellationToken>()))
            .Returns((
                HttpContext context,
                string destinationPrefix,
                HttpMessageInvoker _,
                ForwarderRequestConfig _,
                HttpTransformer transformer,
                CancellationToken cancellationToken) =>
                CaptureAsync(context, destinationPrefix, transformer, cancellationToken));

        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder => {
                webBuilder.UseTestServer();
                webBuilder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddConfiguration(CreateConfiguration()));
                webBuilder.ConfigureServices((context, services) => {
                    services.AddBffReverseProxy(context.Configuration);
                    services.AddSingleton(authentication.Object);
                    services.AddSingleton(forwarder.Object);
                });
                webBuilder.Configure(app => {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapReverseProxy());
                });
            })
            .StartAsync();

        var response = await host.GetTestClient().GetAsync(new Uri("/api/motorcycles", UriKind.Relative));

        response.IsSuccessStatusCode.Should().BeTrue();
        return (capturedHost, capturedAuthorization);
    }
}
