using Microsoft.Extensions.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Application.Pipeline;
using MotorcycleRAG.Application.Pipeline.Audit;
using MotorcycleRAG.Application.Pipeline.Validators;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Persistence.ExternalServices;
using MotorcycleRAG.Persistence.Azure;

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
        services.Configure<IngestionOptions>(configuration.GetSection("Ingestion"));

        // Register pipeline services from Application layer
        services.AddScoped<IDataPipelineOrchestrator, MotorcycleRAG.Application.Pipeline.DataPipelineOrchestrator>();
        services.AddScoped<IFileUploadService, MotorcycleRAG.Application.Pipeline.FileUploadService>();
        services.AddSingleton<IPipelineMonitoringService, MotorcycleRAG.Application.Pipeline.PipelineMonitoringService>();
        services.AddSingleton<IScheduledPipelineService, MotorcycleRAG.Application.Pipeline.ScheduledPipelineService>();

        // Register ingestion job service for Fabric pipeline integration
        services.AddScoped<IIngestionJobService, MotorcycleRAG.Application.Pipeline.IngestionJobService>();

        // Register local and Fabric pipeline services
        services.AddScoped<ILocalPipelineService, MotorcycleRAG.Persistence.ExternalServices.LocalPipelineService>();
        services.AddScoped<IFabricPipelineService, MotorcycleRAG.Persistence.ExternalServices.FabricPipelineService>();
        services.AddScoped<IGraphEntityIngestionService, MotorcycleRAG.Application.Pipeline.GraphEntityIngestionService>();

        // Register named HTTP clients for pipeline services with resilience policies
        // (configured in MotorcycleRAG.Persistence.Azure.ServiceCollectionExtensions.AddPipelineHttpClients)
        services.AddPipelineHttpClients();

        // Register Fabric pipeline services
        services.AddScoped<IManualPageQueryService, ManualPageQueryService>();
        services.AddScoped<SpecsIngestionService>();
        services.AddScoped<ManualBikeLinker>();
        services.AddScoped<IIngestionAuditLogger, IngestionAuditLogger>();
        services.AddScoped<IngestionJobValidator>();

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
