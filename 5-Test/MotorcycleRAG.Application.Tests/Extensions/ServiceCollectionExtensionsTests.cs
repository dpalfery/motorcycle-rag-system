using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Application.Extensions;
using MotorcycleRAG.Application.Services.Caching;
using MotorcycleRAG.Application.Services.Optimization;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.UnitTests.Extensions;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddMotorcycleCaching_WithoutRedis_RegistersBoundOptionsMemoryCacheAndMemoryImplementation()
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Cache:MaxCacheEntries"] = "42",
            ["Cache:EnableCompression"] = "false",
        });

        var returnedServices = services.AddMotorcycleCaching(configuration);

        returnedServices.Should().BeSameAs(services);
        GetRegistration<IQueryCacheService>(services).ImplementationType.Should().Be(typeof(MemoryQueryCacheService));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<CacheConfiguration>>().Value.Should().Match<CacheConfiguration>(options =>
            options.MaxCacheEntries == 42 && !options.EnableCompression);
        provider.GetRequiredService<IMemoryCache>().Should().BeOfType<MemoryCache>();
    }

    [Fact]
    public void AddMotorcycleCaching_WithRedisConnectionString_RegistersDistributedCacheWithoutOpeningAConnection()
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Redis"] = "localhost:6379,password=not-used-by-this-registration-test",
        });

        services.AddMotorcycleCaching(configuration);

        GetRegistration<IQueryCacheService>(services).ImplementationType.Should().Be(typeof(DistributedQueryCacheService));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IDistributedCache));
    }

    [Fact]
    public void AddPerformanceOptimization_RegistersSingletonServicesAndConfiguredNamedHttpClients()
    {
        var services = new ServiceCollection();

        var returnedServices = services.AddPerformanceOptimization(CreateConfiguration());

        returnedServices.Should().BeSameAs(services);
        GetRegistration<IVectorCompressionService>(services).Should().Match<ServiceDescriptor>(descriptor =>
            descriptor.ImplementationType == typeof(VectorCompressionService) && descriptor.Lifetime == ServiceLifetime.Singleton);
        GetRegistration<IBatchProcessingService>(services).Should().Match<ServiceDescriptor>(descriptor =>
            descriptor.ImplementationType == typeof(BatchProcessingService) && descriptor.Lifetime == ServiceLifetime.Singleton);
        GetRegistration<IConnectionPoolService>(services).Should().Match<ServiceDescriptor>(descriptor =>
            descriptor.ImplementationType == typeof(ConnectionPoolService) && descriptor.Lifetime == ServiceLifetime.Singleton);

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        using var azureOpenAi = factory.CreateClient("AzureOpenAI");
        azureOpenAi.Timeout.Should().Be(TimeSpan.FromMinutes(5));
        azureOpenAi.DefaultRequestHeaders.UserAgent.ToString().Should().Be("MotorcycleRAG/1.0");

        using var azureSearch = factory.CreateClient("AzureSearch");
        azureSearch.Timeout.Should().Be(TimeSpan.FromMinutes(2));
        azureSearch.DefaultRequestHeaders.UserAgent.ToString().Should().Be("MotorcycleRAG/1.0");

        using var webSearch = factory.CreateClient("WebSearch");
        webSearch.Timeout.Should().Be(TimeSpan.FromSeconds(30));
        webSearch.DefaultRequestHeaders.UserAgent.ToString().Should().Be("MotorcycleRAG-Bot/1.0");
    }

    [Fact]
    public void AddCachingAndOptimization_RegistersBothServiceGroups()
    {
        var services = new ServiceCollection();

        var returnedServices = services.AddCachingAndOptimization(CreateConfiguration());

        returnedServices.Should().BeSameAs(services);
        GetRegistration<IQueryCacheService>(services).ImplementationType.Should().Be(typeof(MemoryQueryCacheService));
        GetRegistration<IVectorCompressionService>(services).ImplementationType.Should().Be(typeof(VectorCompressionService));
    }

    [Theory]
    [InlineData("AddMotorcycleCaching")]
    [InlineData("AddPerformanceOptimization")]
    [InlineData("AddCachingAndOptimization")]
    public void CacheRegistrationExtensions_WithNullConfiguration_ThrowArgumentNullException(string extensionName)
    {
        var services = new ServiceCollection();

        Action act = extensionName switch
        {
            "AddMotorcycleCaching" => () => services.AddMotorcycleCaching(null!),
            "AddPerformanceOptimization" => () => services.AddPerformanceOptimization(null!),
            _ => () => services.AddCachingAndOptimization(null!),
        };

        act.Should().Throw<ArgumentNullException>();
    }

    private static ServiceDescriptor GetRegistration<TService>(IServiceCollection services) =>
        services.Single(descriptor => descriptor.ServiceType == typeof(TService));

    private static IConfiguration CreateConfiguration(IReadOnlyDictionary<string, string?>? values = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
