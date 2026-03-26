using Microsoft.Extensions.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Persistence.Azure;
using MotorcycleRAG.Persistence.HealthChecks;
using MotorcycleRAG.API.Configuration;

namespace MotorcycleRAG.API.Configuration.Services;

/// <summary>
/// Configuration for Azure AI services
/// </summary>
internal static class AzureAIServiceConfiguration
{
    /// <summary>
    /// Configure Azure AI services
    /// </summary>
    internal static IServiceCollection AddAzureAIServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure Azure Foundry settings with validation
        services.AddOptions<AzureFoundryOptions>()
            .Bind(configuration.GetSection("AzureAI"))
            .ValidateOnStart();
        services.AddOptions<SearchOptions>()
            .Bind(configuration.GetSection("Search"));
        services.AddOptions<TelemetryOptions>()
            .Bind(configuration.GetSection("ApplicationInsights"));

        // Add options validation
        services.AddSingleton<IValidateOptions<AzureFoundryOptions>, AzureFoundryConfigurationValidator>();
        services.AddSingleton<IValidateOptions<SearchOptions>, SearchConfigurationValidator>();
        services.AddSingleton<IValidateOptions<TelemetryOptions>, TelemetryConfigurationValidator>();

        // Register Azure service clients (now implemented in Infrastructure layer)
        services.AddAzureServices(configuration);

        // Register Foundry agent runner (drives thread/run lifecycle with Azure AI Agents Persistent SDK)
        services.AddScoped<IFoundryAgentRunner, MotorcycleRAG.Persistence.Azure.FoundryAgentRunner>();

        return services;
    }

    /// <summary>
    /// Configure health checks for Azure AI services
    /// </summary>
    internal static IHealthChecksBuilder AddAzureAIHealthChecks(this IHealthChecksBuilder builder, IConfiguration configuration)
    {
        // Add dependency-specific health checks
        builder.AddCheck<AzureSearchHealthCheck>("azure_ai_search");

        builder.AddCheck<AzureFoundryHealthCheck>("azure_foundry");

        builder.AddCheck<DocumentIntelligenceHealthCheck>("azure_document_intelligence");

        return builder;
    }
}
