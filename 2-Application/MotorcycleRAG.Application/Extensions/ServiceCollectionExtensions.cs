using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MotorcycleRAG.Application.Caching;
using MotorcycleRAG.Application.Optimization;
using MotorcycleRAG.Contracts.Interfaces;


namespace MotorcycleRAG.Application.Extensions;

/// <summary>
/// Extension methods for configuring caching and performance optimization services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds caching services to the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">Configuration instance</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddMotorcycleCaching(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Configure cache settings
        services.Configure<CacheConfiguration>(configuration.GetSection("Cache"));

        // Add memory cache
        services.AddMemoryCache(options =>
        {
            var cacheConfig = configuration.GetSection("Cache").Get<CacheConfiguration>() ?? new CacheConfiguration();
            options.SizeLimit = cacheConfig.MaxCacheEntries;
        });

        // Add distributed cache (Redis) if configured
        var redisConnectionString = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrEmpty(redisConnectionString))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnectionString;
                options.InstanceName = "MotorcycleRAG";
            });
            
            // Use distributed cache implementation
            services.AddSingleton<IQueryCacheService, DistributedQueryCacheService>();
        }
        else
        {
            // Use in-memory cache implementation
            services.AddSingleton<IQueryCacheService, MemoryQueryCacheService>();
        }

        return services;
    }

    /// <summary>
    /// Adds performance optimization services to the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">Configuration instance</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddPerformanceOptimization(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        // Add vector compression service
        services.AddSingleton<IVectorCompressionService, VectorCompressionService>();

        // Add batch processing service
        services.AddSingleton<IBatchProcessingService, BatchProcessingService>();

        // Add connection pool service
        services.AddSingleton<IConnectionPoolService, ConnectionPoolService>();

        // Configure HTTP clients with optimized settings
        services.AddHttpClient("AzureOpenAI", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
            client.DefaultRequestHeaders.Add("User-Agent", "MotorcycleRAG/1.0");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            MaxConnectionsPerServer = 10,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            UseCookies = false
        });

        services.AddHttpClient("AzureSearch", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(2);
            client.DefaultRequestHeaders.Add("User-Agent", "MotorcycleRAG/1.0");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            MaxConnectionsPerServer = 5,
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
            UseCookies = false
        });

        services.AddHttpClient("WebSearch", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("User-Agent", "MotorcycleRAG-Bot/1.0");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            MaxConnectionsPerServer = 3,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            UseCookies = false
        });

        return services;
    }

    /// <summary>
    /// Adds all caching and optimization services.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">Configuration instance</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddCachingAndOptimization(this IServiceCollection services, IConfiguration configuration)
    {
        return services
            .AddMotorcycleCaching(configuration)
            .AddPerformanceOptimization(configuration);
    }
}