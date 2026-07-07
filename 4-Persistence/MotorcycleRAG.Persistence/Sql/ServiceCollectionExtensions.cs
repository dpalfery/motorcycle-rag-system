using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Repositories;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Persistence.Sql.Repositories;
using MotorcycleRAG.Persistence.Configuration;



namespace MotorcycleRAG.Persistence.Sql;

/// <summary>
/// Extension methods for registering SQL persistence services in DI container
/// </summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Register SQL persistence services with connection management
    /// Note: Connection string must be provided through Azure App Configuration and Key Vault.
    /// </summary>
    public static IServiceCollection AddSqlPersistenceServices(
        this IServiceCollection services,
        IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        // Configure SQL options — ValidateOnStart ensures a missing connection string fails at boot,
        // not silently at health-check or first query time.
        services.AddOptions<SqlOptions>()
            .Bind(configuration.GetSection("Sql"))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<SqlOptions>, SqlOptionsValidator>();

        // Register SQL connection factory
        services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();

        // Register SQL repositories
        services.AddScoped<IIngestionJobRepository, IngestionJobRepository>();
        services.AddScoped<IGraphRepository, SqlGraphRepository>();
        services.AddScoped<IBikeModelRepository, BikeModelRepository>();
        services.AddScoped<IAuditRepository, AuditRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUsageRepository, UsageRepository>();
        services.AddScoped<IPlanRepository, PlanRepository>();
        services.AddScoped<IWebSourceRepository, WebSourceRepository>();
        services.AddScoped<IManualDocumentRepository, ManualDocumentRepository>();

        // Register web trust policy store for domain allowlist enforcement
        services.AddSingleton<WebTrustPolicyStore>();
        services.AddSingleton<IWebTrustPolicyStore>(sp => sp.GetRequiredService<WebTrustPolicyStore>());

        return services;
    }
}

/// <summary>
/// Validates SQL configuration on startup
/// </summary>
public class SqlOptionsValidator : IValidateOptions<SqlOptions> {
    public ValidateOptionsResult Validate(string? name, SqlOptions options) {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            failures.Add("Sql:ConnectionString is required. In Development add it to user secrets; in Production it must come from Azure App Configuration + Key Vault.");

        if (options.CommandTimeout <= 0)
            failures.Add("Sql:CommandTimeout must be greater than 0");

        if (options.ConnectionTimeout <= 0)
            failures.Add("Sql:ConnectionTimeout must be greater than 0");

        if (options.MaxPoolSize <= 0)
            failures.Add("Sql:MaxPoolSize must be greater than 0");

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
