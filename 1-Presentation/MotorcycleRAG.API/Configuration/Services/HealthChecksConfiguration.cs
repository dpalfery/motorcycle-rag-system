using Microsoft.Extensions.Diagnostics.HealthChecks;
using MotorcycleRAG.Persistence.HealthChecks;

namespace MotorcycleRAG.API.Configuration.Services;

/// <summary>
/// Configuration for health checks
/// </summary>
internal static class HealthChecksConfiguration
{
    /// <summary>
    /// Configure health checks for all critical dependencies
    /// </summary>
    internal static IHealthChecksBuilder AddHealthChecks(this IHealthChecksBuilder builder, IConfiguration configuration)
    {
        // Self health check
        builder.AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("API is running"));

        // Azure AI Search health check
        builder.AddCheck<AzureSearchHealthCheck>("azure_search");

        // Azure OpenAI health check
        builder.AddCheck<AzureOpenAIHealthCheck>("azure_openai");

        // Azure Document Intelligence health check
        builder.AddCheck<DocumentIntelligenceHealthCheck>("azure_document_intelligence");

        // Azure Foundry health check
        builder.AddCheck<AzureFoundryHealthCheck>("azure_foundry");

        // SQL Database health check
        builder.AddCheck<SqlDatabaseHealthCheck>("sql_database");

        // Add configuration validation health check
        builder.AddCheck("configuration", () => ValidateConfiguration(configuration));

        return builder;
    }

    /// <summary>
    /// Validate overall configuration health
    /// </summary>
    private static Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult ValidateConfiguration(IConfiguration configuration)
    {
        try
        {
            var issues = new List<string>();

            // Check required configuration sections
            if (!configuration.GetSection("AzureAI").Exists())
                issues.Add("AzureAI configuration section is missing");

            if (!configuration.GetSection("Search").Exists())
                issues.Add("Search configuration section is missing");

            // Check Application Insights configuration
            var appInsightsConnectionString = configuration.GetConnectionString("ApplicationInsights")
                ?? configuration["ApplicationInsights:ConnectionString"];

            if (string.IsNullOrWhiteSpace(appInsightsConnectionString))
                issues.Add("Application Insights connection string is not configured");

            return issues.Count == 0
                ? Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("All configuration sections are present")
                : Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Degraded($"Configuration issues: {string.Join(", ", issues)}");
        }
        catch (Exception ex)
        {
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("Configuration validation failed", ex);
        }
    }
}
