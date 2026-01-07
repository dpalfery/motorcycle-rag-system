using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Persistence.Sql;
using MotorcycleRAG.Persistence.Sql.Repositories;
using MotorcycleRAG.Application.Services;

namespace MotorcycleRAG.API.Configuration.Services;

/// <summary>
/// Configuration for SQL persistence services
/// </summary>
internal static class PersistenceConfiguration
{
    /// <summary>
    /// Configure SQL persistence services
    /// </summary>
    internal static IServiceCollection AddSqlPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure SQL options
        services.Configure<SqlOptions>(configuration.GetSection("Sql"));
        services.AddSingleton<IValidateOptions<SqlOptions>, SqlOptionsValidator>();

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

        return services;
    }
}
