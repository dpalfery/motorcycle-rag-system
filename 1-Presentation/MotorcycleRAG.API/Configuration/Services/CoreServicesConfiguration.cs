using Microsoft.Extensions.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Application.Services.Citations;
using MotorcycleRAG.Application.Services.QueryProcessing;
using MotorcycleRAG.Application.Services.ResponseProcessing;
using MotorcycleRAG.Application.Services.Metrics;

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

        // Register extracted services for MotorcycleRagService
        services.AddScoped<ClaimCitationService>();
        services.AddScoped<QueryRefinementService>();
        services.AddScoped<ResponseLimitationAnalyzer>();
        services.AddScoped<QueryCostCalculator>();

        // Add Application Insights TelemetryClient
        services.AddApplicationInsightsTelemetry();
        services.AddSingleton<ITelemetryService, MotorcycleRAG.Persistence.Telemetry.TelemetryService>();

        return services;
    }
}
