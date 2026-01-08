using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Net.NetworkInformation;
using MotorcycleRAG.Persistence.Azure;
using MotorcycleRAG.Persistence.Sql;
using MotorcycleRAG.Persistence.Sql.Repositories;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Persistence.DataProcessing;
using MotorcycleRAG.Application.Services;

namespace MotorcycleRAG.API.Configuration;

/// <summary>
/// Extension methods for configuring services in the DI container
/// </summary>
internal static class ServiceConfiguration {
    private static readonly string[] AzureSearchTags = new[] { "azure", "search" };
    private static readonly string[] AzureOpenAITags = new[] { "azure", "ai" };
    private static readonly string[] AzureDocumentTags = new[] { "azure", "document" };
    private static readonly string[] SqlDatabaseTags = new[] { "database", "sql" };
    private static readonly string[] AzureFoundryTags = new[] { "azure", "foundry" };

    /// <summary>
    /// Configure Azure AI services
    /// </summary>
    internal static IServiceCollection AddAzureAIServices(this IServiceCollection services, IConfiguration configuration) {
        // Configure Azure AI settings with validation
        services.Configure<AzureAIOptions>(configuration.GetSection("AzureAI"));
        services.Configure<SearchOptions>(configuration.GetSection("Search"));
        services.Configure<TelemetryOptions>(configuration.GetSection("ApplicationInsights"));

        // Add options validation
        services.AddSingleton<IValidateOptions<AzureAIOptions>, AzureAIConfigurationValidator>();
        services.AddSingleton<IValidateOptions<SearchOptions>, SearchConfigurationValidator>();
        services.AddSingleton<IValidateOptions<TelemetryOptions>, TelemetryConfigurationValidator>();

        // Register Azure service clients (now implemented in Infrastructure layer)
        services.AddAzureServices(configuration);

        return services;
    }

    /// <summary>
    /// Configure core application services
    /// </summary>
    internal static IServiceCollection AddCoreServices(this IServiceCollection services) {
        // Register core service interfaces to concrete implementations in Application layer
        services.AddScoped<IMotorcycleRagService, MotorcycleRAG.Application.Services.MotorcycleRagService>();
        
        // Register extracted services for AgentOrchestrator
        services.AddScoped<MotorcycleRAG.Application.Services.Mcp.McpToolManager>();
        services.AddScoped<MotorcycleRAG.Application.Services.Telemetry.DegradedModeTracker>();
        services.AddScoped<MotorcycleRAG.Application.Services.SearchResultFusionService>();
        
        // Register AgentOrchestrator with its dependencies
        services.AddScoped<IAgentOrchestrator>(provider => {
            var agents = provider.GetRequiredService<IEnumerable<MotorcycleRAG.Contracts.Interfaces.ISearchAgent>>();
            var openAIClient = provider.GetRequiredService<MotorcycleRAG.Contracts.Interfaces.IAzureOpenAIClient>();
            var searchConfig = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<MotorcycleRAG.Core.Options.SearchOptions>>();
            var logger = provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MotorcycleRAG.Application.Services.AgentOrchestrator>>();
            var mcpToolManager = provider.GetRequiredService<MotorcycleRAG.Application.Services.Mcp.McpToolManager>();
            var degradedModeTracker = provider.GetRequiredService<MotorcycleRAG.Application.Services.Telemetry.DegradedModeTracker>();
            var resultFusionService = provider.GetRequiredService<MotorcycleRAG.Application.Services.SearchResultFusionService>();
            var correlationService = provider.GetRequiredService<MotorcycleRAG.Contracts.Interfaces.ICorrelationService>();

            return new MotorcycleRAG.Application.Services.AgentOrchestrator(
                agents,
                openAIClient,
                searchConfig,
                logger,
                mcpToolManager,
                degradedModeTracker,
                resultFusionService,
                correlationService);
        });

        // Add Application Insights TelemetryClient
        services.AddApplicationInsightsTelemetry();
        services.AddSingleton<ITelemetryService, MotorcycleRAG.Persistence.Telemetry.TelemetryService>();

        return services;
    }

    /// <summary>
    /// Configure search agents
    /// </summary>
    internal static IServiceCollection AddSearchAgents(this IServiceCollection services) {
        // Register search agent implementations from Application layer
        // Note: QueryPlannerAgent is registered separately to avoid circular dependency
        services.AddScoped<ISearchAgent, MotorcycleRAG.Application.Agents.VectorSearchAgent>();

        // Register extracted web search services
        services.AddScoped<MotorcycleRAG.Application.Services.Web.WebSearchRateLimiter>();
        services.AddScoped<MotorcycleRAG.Application.Services.Web.WebSearchCache>();
        services.AddScoped<MotorcycleRAG.Application.Services.Web.WebContentExtractor>();
        services.AddScoped<MotorcycleRAG.Application.Services.Web.WebSearchTermEnhancer>();
        services.AddScoped<MotorcycleRAG.Application.Services.Web.WebSourceValidator>();

        // Register WebSearchAgent with optional IWebTrustPolicyStore for trust tier filtering
        services.AddScoped<ISearchAgent>(provider => {
            var httpClient = provider.GetRequiredService<HttpClient>();
            var config = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<MotorcycleRAG.Core.Options.WebSearchOptions>>();
            var logger = provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MotorcycleRAG.Application.Agents.WebSearchAgent>>();
            var rateLimiter = provider.GetRequiredService<MotorcycleRAG.Application.Services.Web.WebSearchRateLimiter>();
            var cache = provider.GetRequiredService<MotorcycleRAG.Application.Services.Web.WebSearchCache>();
            var extractor = provider.GetRequiredService<MotorcycleRAG.Application.Services.Web.WebContentExtractor>();
            var termEnhancer = provider.GetRequiredService<MotorcycleRAG.Application.Services.Web.WebSearchTermEnhancer>();
            var validator = provider.GetRequiredService<MotorcycleRAG.Application.Services.Web.WebSourceValidator>();

            return new MotorcycleRAG.Application.Agents.WebSearchAgent(httpClient, config, logger, rateLimiter, cache, extractor, termEnhancer, validator);
        });

        services.AddScoped<IQueryPlannerAgent, MotorcycleRAG.Application.Agents.QueryPlannerAgent>();

        return services;
    }

    /// <summary>
    /// Configure data processors
    /// </summary>
    internal static IServiceCollection AddDataProcessors(this IServiceCollection services) {
        // Register data processor implementations from Persistence layer
        services.AddScoped<IDataProcessor<CSVFile>, MotorcycleCsvProcessor>();
        services.AddScoped<IDataProcessor<PDFDocument>, MotorcyclePdfProcessor>();

        return services;
    }

    /// <summary>
    /// Configure data pipeline services
    /// </summary>
    internal static IServiceCollection AddDataPipelineServices(this IServiceCollection services, IConfiguration configuration) {
        // Configure pipeline settings
        services.Configure<MotorcycleRAG.Application.Pipeline.PipelineConfiguration>(configuration.GetSection("Pipeline"));
        services.Configure<MotorcycleRAG.Application.Pipeline.FileUploadConfiguration>(configuration.GetSection("FileUpload"));
        services.Configure<MotorcycleRAG.Application.Pipeline.PipelineMonitoringConfiguration>(configuration.GetSection("PipelineMonitoring"));
        services.Configure<MotorcycleRAG.Application.Pipeline.ScheduledProcessingConfiguration>(configuration.GetSection("ScheduledProcessing"));

        // Register pipeline services from Application layer
        services.AddScoped<IDataPipelineOrchestrator, MotorcycleRAG.Application.Pipeline.DataPipelineOrchestrator>();
        services.AddScoped<IFileUploadService, MotorcycleRAG.Application.Pipeline.FileUploadService>();
        services.AddSingleton<IPipelineMonitoringService, MotorcycleRAG.Application.Pipeline.PipelineMonitoringService>();
        services.AddSingleton<IScheduledPipelineService, MotorcycleRAG.Application.Pipeline.ScheduledPipelineService>();

        // Register the scheduled service as a hosted service
        services.AddHostedService<MotorcycleRAG.Application.Pipeline.ScheduledPipelineService>();

        return services;
    }

    /// <summary>
    /// Configure health checks for all critical dependencies
    /// </summary>
    internal static IServiceCollection AddHealthChecks(this IServiceCollection services, IConfiguration configuration) {
        var healthChecksBuilder = services.AddHealthChecks()
            .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("API is running"))
            .AddCheck("configuration", () => ValidateConfiguration(configuration));

        // Add dependency-specific health checks
        // Azure AI Search health check
        healthChecksBuilder.AddCheck<MotorcycleRAG.Persistence.HealthChecks.AzureSearchHealthCheck>(
            "azure_ai_search",
            failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy,
            tags: AzureSearchTags,
            timeout: TimeSpan.FromSeconds(5));

        // Azure OpenAI health check
        healthChecksBuilder.AddCheck<MotorcycleRAG.Persistence.HealthChecks.AzureOpenAIHealthCheck>(
            "azure_openai",
            failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy,
            tags: AzureOpenAITags,
            timeout: TimeSpan.FromSeconds(10));

        // Azure Document Intelligence health check
        healthChecksBuilder.AddCheck<MotorcycleRAG.Persistence.HealthChecks.DocumentIntelligenceHealthCheck>(
            "azure_document_intelligence",
            failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy,
            tags: AzureDocumentTags,
            timeout: TimeSpan.FromSeconds(5));

        // SQL Database health check
        healthChecksBuilder.AddCheck<MotorcycleRAG.Persistence.HealthChecks.SqlDatabaseHealthCheck>(
            "sql_database",
            failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy,
            tags: SqlDatabaseTags,
            timeout: TimeSpan.FromSeconds(5));

        // Azure AI Foundry health check
        healthChecksBuilder.AddCheck<MotorcycleRAG.Persistence.HealthChecks.AzureFoundryHealthCheck>(
            "azure_foundry",
            failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded,
            tags: AzureFoundryTags,
            timeout: TimeSpan.FromSeconds(5));

        // Configure health check response caching to prevent health check storms
        // Cache successful responses for 30 seconds
        services.Configure<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckPublisherOptions>(options => {
            options.Delay = TimeSpan.FromSeconds(30);
            options.Period = TimeSpan.FromSeconds(30);
        });

        return services;
    }

    /// <summary>
    /// Configure SQL persistence services
    /// </summary>
    internal static IServiceCollection AddSqlPersistence(this IServiceCollection services, IConfiguration configuration) {
        // Configure SQL options
        services.Configure<MotorcycleRAG.Core.Options.SqlOptions>(configuration.GetSection("Sql"));
        services.AddSingleton<IValidateOptions<MotorcycleRAG.Core.Options.SqlOptions>, SqlOptionsValidator>();

        // Register SQL connection factory
        services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();

        // Register repository implementations
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUsageRepository, UsageRepository>();
        services.AddScoped<IWebSourceRepository, WebSourceRepository>();
        services.AddScoped<IAuditRepository, AuditRepository>();
        services.AddScoped<IWebScrapeRunRepository, MotorcycleRAG.Persistence.Sql.Repositories.WebScrapeRunRepository>();

        // Register application services
        services.AddScoped<WebSourceRegistryService>();
        services.AddScoped<IWebScrapeOrchestrator, MotorcycleRAG.Application.Services.WebScrapeOrchestrator>();

        // MCP Tool Configuration
        services.AddScoped<IToolConfigurationRepository, MotorcycleRAG.Persistence.Sql.Repositories.ToolConfigurationRepository>();
        services.AddScoped<IToolConfigurationAuditRepository, MotorcycleRAG.Persistence.Sql.Repositories.ToolConfigurationAuditRepository>();
        services.AddScoped<IToolConfigurationService, MotorcycleRAG.Application.Services.ToolConfigurationService>();
        services.AddScoped<IMcpConfigurationProvider, MotorcycleRAG.Application.Services.McpConfigurationProvider>();

        return services;
    }

    /// <summary>
    /// Configure web trust policy services
    /// </summary>
    internal static IServiceCollection AddWebTrustPolicyServices(this IServiceCollection services, IConfiguration configuration) {
        // Register web trust policy store as singleton
        services.AddSingleton<IWebTrustPolicyStore, MotorcycleRAG.Persistence.Configuration.WebTrustPolicyStore>();

        return services;
    }

    /// <summary>
    /// Validate overall configuration health
    /// </summary>
    internal static Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult ValidateConfiguration(IConfiguration configuration) {
        try {
            var issues = new List<string>();

            // Check required configuration sections
            var azureSection = configuration.GetSection("AzureAI");
            if (!azureSection.Exists())
                issues.Add("AzureAI configuration section is missing");

            var searchSection = configuration.GetSection("Search");
            if (!searchSection.Exists())
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
        catch (Exception ex) {
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("Configuration validation failed", ex);
        }
    }
}