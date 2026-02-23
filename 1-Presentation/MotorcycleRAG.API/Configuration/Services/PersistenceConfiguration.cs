using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Persistence.Sql;
using MotorcycleRAG.Persistence.Sql.Repositories;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Persistence.Resilience;
using MotorcycleRAG.Persistence.Azure;

namespace MotorcycleRAG.API.Configuration.Services;

/// <summary>
/// Configuration for persistence services (SQL + Azure Storage)
/// </summary>
internal static class PersistenceConfiguration
{
    /// <summary>
    /// Configure persistence services (SQL repositories + Azure Blob Storage)
    /// </summary>
    internal static IServiceCollection AddSqlPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure SQL options
        services.Configure<SqlOptions>(configuration.GetSection("Sql"));
        services.AddSingleton<IValidateOptions<SqlOptions>, SqlOptionsValidator>();
        // Configure Azure Blob Storage options
        services.Configure<BlobStorageOptions>(configuration.GetSection("BlobStorage"));

        // Register Azure Blob Storage service
        services.AddScoped<IBlobStorageService, AzureBlobStorageService>();

        // Register SQL connection factory
        services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();

        // Register repository implementations
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUsageRepository, UsageRepository>();
        services.AddScoped<IWebSourceRepository, WebSourceRepository>();
        services.AddScoped<IAuditRepository, AuditRepository>();
        services.AddScoped<IWebScrapeRunRepository, WebScrapeRunRepository>();

        // Register application services
        services.AddScoped<WebSourceRegistryService>();
        services.AddScoped<IWebScrapeOrchestrator, WebScrapeOrchestrator>();

        // MCP Tool Configuration
        services.AddScoped<IToolConfigurationRepository, ToolConfigurationRepository>();
        services.AddScoped<IToolConfigurationAuditRepository, ToolConfigurationAuditRepository>();
        services.AddScoped<IToolConfigurationService, ToolConfigurationService>();
        services.AddScoped<IMcpConfigurationProvider, McpConfigurationProvider>();

        // Correlation and telemetry services
        services.AddSingleton<ICorrelationService, CorrelationService>();

        return services;
    }
}
