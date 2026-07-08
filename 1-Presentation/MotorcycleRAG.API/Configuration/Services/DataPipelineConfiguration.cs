using Microsoft.Extensions.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Application.Services.Ingestion;
using MotorcycleRAG.Application.Services.Ingestion.Audit;
using MotorcycleRAG.Application.Features.Ingestion.Validators;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Persistence.Azure;

namespace MotorcycleRAG.API.Configuration.Services;

/// <summary>
/// Configuration for data pipeline services
/// </summary>
internal static class DataPipelineConfiguration {
    /// <summary>
    /// Configure data pipeline services
    /// </summary>
    internal static IServiceCollection AddDataPipelineServices(this IServiceCollection services, IConfiguration configuration) {
        // Configure pipeline settings
        services.Configure<PipelineConfiguration>(configuration.GetSection("Pipeline"));
        services.Configure<FileUploadConfiguration>(configuration.GetSection("FileUpload"));
        services.Configure<PipelineMonitoringConfiguration>(configuration.GetSection("PipelineMonitoring"));
        services.Configure<ScheduledProcessingConfiguration>(configuration.GetSection("ScheduledProcessing"));
        services.Configure<IngestionOptions>(configuration.GetSection("Ingestion"));

        // Register pipeline services from Application layer
        services.AddScoped<IDataPipelineOrchestrator, MotorcycleRAG.Application.Services.Ingestion.DataPipelineOrchestrator>();
        services.AddScoped<IFileUploadService, MotorcycleRAG.Application.Services.Ingestion.FileUploadService>();
        services.AddSingleton<IPipelineMonitoringService, MotorcycleRAG.Application.Services.Ingestion.PipelineMonitoringService>();
        services.AddSingleton<IScheduledPipelineService, MotorcycleRAG.Application.Services.Ingestion.ScheduledPipelineService>();

        // Register ingestion job service for Fabric pipeline integration
        services.AddScoped<IIngestionJobService, MotorcycleRAG.Application.Services.Ingestion.IngestionJobService>();

        // Singleton bounded channel shared between the scoped IngestionJobService (producer) and
        // GraphIngestionBackgroundService (single consumer). Must be a singleton so that all
        // request-scoped writers and the hosted reader observe the same queue.
        services.AddSingleton<GraphIngestionChannel>();

        // Register the job deletion background service (hosted). It polls for
        // Deleting ingestion jobs and performs best-effort artifact cleanup outside
        // the originating HTTP request lifecycle. Resolves scoped dependencies
        // (IIngestionJobService / IIngestionJobRepository) through IServiceScopeFactory.
        services.AddHostedService<JobDeletionBackgroundService>();

        // Drains GraphIngestionChannel and runs graph ingestion outside the HTTP request
        // lifecycle. Resolves scoped IIngestionJobService per job via IServiceScopeFactory.
        services.AddHostedService<GraphIngestionBackgroundService>();

        services.AddSingleton<IIngestionSourceAccessTokenService, MotorcycleRAG.Application.Services.Ingestion.IngestionSourceAccessTokenService>();
        services.AddMemoryCache();
        services.AddScoped<IManualIngestionService, MotorcycleRAG.Application.Services.Ingestion.ManualIngestionService>();
        services.AddScoped<IChunkReprocessService, ChunkReprocessService>();

        services.AddScoped<IGraphEntityIngestionService, MotorcycleRAG.Application.Services.Ingestion.GraphEntityIngestionService>();

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
    internal static IHealthChecksBuilder AddDataPipelineHealthChecks(this IHealthChecksBuilder builder, IConfiguration configuration) {
        // Add pipeline health checks
        builder.AddCheck("data_pipeline", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("Data pipeline is running"));

        return builder;
    }
}
