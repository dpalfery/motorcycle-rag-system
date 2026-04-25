using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Persistence.Sql;
using MotorcycleRAG.Persistence.Sql.Repositories;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Persistence.Resilience;
using MotorcycleRAG.Persistence.Azure;
using MotorcycleRAG.Persistence.Notifications;

namespace MotorcycleRAG.API.Configuration.Services;

/// <summary>
/// Configuration for persistence services (SQL + Azure Storage)
/// </summary>
internal static class PersistenceConfiguration {
    /// <summary>
    /// Configure persistence services (SQL repositories + Azure Blob Storage)
    /// </summary>
    internal static IServiceCollection AddSqlPersistence(this IServiceCollection services, IConfiguration configuration) {
        // Configure SQL options
        services.Configure<SqlOptions>(configuration.GetSection("Sql"));
        services.AddSingleton<IValidateOptions<SqlOptions>, SqlOptionsValidator>();
        // Configure Azure Blob Storage options
        services.Configure<BlobStorageOptions>(configuration.GetSection("BlobStorage"));
        services.Configure<ExternalIdentityProvisioningOptions>(configuration.GetSection("ExternalIdentityProvisioning"));

        // Register Azure Blob Storage service
        services.AddScoped<IBlobStorageService, AzureBlobStorageService>();

        // Register SQL connection factory
        services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();

        // Register repository implementations
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUsageRepository, UsageRepository>();
        services.AddScoped<IAccessRequestRepository, AccessRequestRepository>();
        services.AddScoped<IUserIdentityRepository, UserIdentityRepository>();
        services.AddScoped<IUserManagementQueryRepository, UserManagementQueryRepository>();
        services.AddScoped<IWebSourceRepository, WebSourceRepository>();
        services.AddScoped<IAuditRepository, AuditRepository>();
        services.AddScoped<IWebScrapeRunRepository, WebScrapeRunRepository>();
        services.AddScoped<IPlanRepository, PlanRepository>();
        services.AddScoped<IApproverNotificationService, ApproverNotificationService>();
        services.AddHttpClient<IExternalIdentityProvisioningService, ExternalIdentityProvisioningService>();

        // Register application services
        services.AddScoped<WebSourceRegistryService>();
        services.AddScoped<IWebScrapeOrchestrator, WebScrapeOrchestrator>();
        services.AddScoped<IPlanPolicyService, PlanPolicyService>();

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
