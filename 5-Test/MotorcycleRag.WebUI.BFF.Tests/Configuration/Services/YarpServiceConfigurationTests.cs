using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MotorcycleRag.WebUI.BFF.Configuration.Services;
using Yarp.ReverseProxy.Configuration;

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
}
