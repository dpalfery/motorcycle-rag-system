using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
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
    /// Note: Connection string must be provided via SQL_CONNECTION_STRING environment variable
    /// </summary>
    public static IServiceCollection AddSqlPersistenceServices(
        this IServiceCollection services,
        IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        // Configure SQL options from appsettings (non-secret settings only)
        services.Configure<SqlOptions>(
            configuration.GetSection("Sql"));

        // Validate SQL configuration on startup
        services.AddSingleton<IValidateOptions<SqlOptions>, SqlOptionsValidator>();

        // Register SQL connection factory
        services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();

        // Register SQL repositories
        //services.AddScoped<IIngestionJobRepository, IngestionJobRepository>(); not yet implemented
        services.AddScoped<IAuditRepository, AuditRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUsageRepository, UsageRepository>();
        services.AddScoped<IPlanRepository, PlanRepository>();
        services.AddScoped<IWebSourceRepository, WebSourceRepository>();

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
