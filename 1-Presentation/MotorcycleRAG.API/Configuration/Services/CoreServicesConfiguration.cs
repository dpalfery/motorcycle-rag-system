using Microsoft.Extensions.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Application.Services;

namespace MotorcycleRAG.API.Configuration.Services;

/// <summary>
/// Configuration for core application services
/// </summary>
internal static class CoreServicesConfiguration
{
    /// <summary>
    /// Configure core application services
    /// </summary>
    internal static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        // Register core service interfaces to concrete implementations in Application layer
        services.AddScoped<IMotorcycleRagService, MotorcycleRAG.Application.Services.MotorcycleRagService>();
        services.AddScoped<IAgentOrchestrator, MotorcycleRAG.Application.Services.AgentOrchestrator>();

        // Add Application Insights TelemetryClient
        services.AddApplicationInsightsTelemetry();
        services.AddSingleton<ITelemetryService, MotorcycleRAG.Persistence.Telemetry.TelemetryService>();

        return services;
    }
}
