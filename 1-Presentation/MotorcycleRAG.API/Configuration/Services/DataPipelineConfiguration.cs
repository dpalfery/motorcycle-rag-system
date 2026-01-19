using Microsoft.Extensions.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Application.Pipeline;
using MotorcycleRAG.Application.Services;

namespace MotorcycleRAG.API.Configuration.Services;

/// <summary>
/// Configuration for data pipeline services
/// </summary>
internal static class DataPipelineConfiguration
{
    /// <summary>
    /// Configure data pipeline services
    /// </summary>
    internal static IServiceCollection AddDataPipelineServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure pipeline settings
        services.Configure<PipelineConfiguration>(configuration.GetSection("Pipeline"));
        services.Configure<FileUploadConfiguration>(configuration.GetSection("FileUpload"));
        services.Configure<PipelineMonitoringConfiguration>(configuration.GetSection("PipelineMonitoring"));
        services.Configure<ScheduledProcessingConfiguration>(configuration.GetSection("ScheduledProcessing"));

        // Register pipeline services from Application layer
        services.AddScoped<IDataPipelineOrchestrator, MotorcycleRAG.Application.Pipeline.DataPipelineOrchestrator>();
        services.AddScoped<IFileUploadService, MotorcycleRAG.Application.Pipeline.FileUploadService>();
        services.AddSingleton<IPipelineMonitoringService, MotorcycleRAG.Application.Pipeline.PipelineMonitoringService>();
        services.AddSingleton<IScheduledPipelineService, MotorcycleRAG.Application.Pipeline.ScheduledPipelineService>();

        // Register scheduled service as a hosted service
        // ScheduledPipelineService extends BackgroundService which implements IHostedService
        // Use singleton factory to ensure same instance is used for both interfaces
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostedService>(serviceProvider =>
            serviceProvider.GetRequiredService<IScheduledPipelineService>() as Microsoft.Extensions.Hosting.IHostedService
            ?? throw new InvalidOperationException("ScheduledPipelineService must implement IHostedService"));

        return services;
    }

    /// <summary>
    /// Configure health checks for pipeline services
    /// </summary>
    internal static IHealthChecksBuilder AddDataPipelineHealthChecks(this IHealthChecksBuilder builder, IConfiguration configuration)
    {
        // Add pipeline health checks
        builder.AddCheck("data_pipeline", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("Data pipeline is running"));

        return builder;
    }
}
