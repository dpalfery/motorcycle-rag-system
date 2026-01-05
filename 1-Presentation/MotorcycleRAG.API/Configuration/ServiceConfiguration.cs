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
public static class ServiceConfiguration {
    /// <summary>
    /// Configure Azure AI services
    /// </summary>
    public static IServiceCollection AddAzureAIServices(this IServiceCollection services, IConfiguration configuration) {
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
    public static IServiceCollection AddCoreServices(this IServiceCollection services) {
        // Register core service interfaces to concrete implementations in Application layer
        services.AddScoped<IMotorcycleRagService, MotorcycleRAG.Application.Services.MotorcycleRagService>();
        services.AddScoped<IAgentOrchestrator, MotorcycleRAG.Application.Services.AgentOrchestrator>();

        // Add Application Insights TelemetryClient
        services.AddApplicationInsightsTelemetry();
        services.AddSingleton<ITelemetryService, MotorcycleRAG.Persistence.Telemetry.TelemetryService>();

        return services;
    }

    /// <summary>
    /// Configure search agents
    /// </summary>
    public static IServiceCollection AddSearchAgents(this IServiceCollection services) {
        // Register search agent implementations from Application layer
        // Note: QueryPlannerAgent is registered separately to avoid circular dependency
        services.AddScoped<ISearchAgent, MotorcycleRAG.Application.Agents.VectorSearchAgent>();

        // Register WebSearchAgent with optional IWebTrustPolicyStore for trust tier filtering
        services.AddScoped<ISearchAgent>(provider => {
            var httpClient = provider.GetRequiredService<HttpClient>();
            var openAIClient = provider.GetRequiredService<MotorcycleRAG.Contracts.Interfaces.IAzureOpenAIClient>();
            var config = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<MotorcycleRAG.Core.Options.WebSearchOptions>>();
            var logger = provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MotorcycleRAG.Application.Agents.WebSearchAgent>>();
            var trustPolicyStore = provider.GetService<MotorcycleRAG.Contracts.Interfaces.IWebTrustPolicyStore>();

            return new MotorcycleRAG.Application.Agents.WebSearchAgent(httpClient, openAIClient, config, logger, trustPolicyStore);
        });

        services.AddScoped<IQueryPlannerAgent, MotorcycleRAG.Application.Agents.QueryPlannerAgent>();

        return services;
    }

    /// <summary>
    /// Configure data processors
    /// </summary>
    public static IServiceCollection AddDataProcessors(this IServiceCollection services) {
        // Register data processor implementations from Persistence layer
        services.AddScoped<IDataProcessor<CSVFile>, MotorcycleCSVProcessor>();
        services.AddScoped<IDataProcessor<PDFDocument>, MotorcyclePDFProcessor>();

        return services;
    }

    /// <summary>
    /// Configure data pipeline services
    /// </summary>
    public static IServiceCollection AddDataPipelineServices(this IServiceCollection services, IConfiguration configuration) {
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
    public static IServiceCollection AddHealthChecks(this IServiceCollection services, IConfiguration configuration) {
        var healthChecksBuilder = services.AddHealthChecks()
            .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("API is running"))
            .AddCheck("configuration", () => ValidateConfiguration(configuration));

        // Add dependency-specific health checks
        // Azure AI Search health check
        healthChecksBuilder.AddCheck<MotorcycleRAG.Persistence.HealthChecks.AzureSearchHealthCheck>(
            "azure_ai_search",
            failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy,
            tags: new[] { "azure", "search" },
            timeout: TimeSpan.FromSeconds(5));

        // Azure OpenAI health check
        healthChecksBuilder.AddCheck<MotorcycleRAG.Persistence.HealthChecks.AzureOpenAIHealthCheck>(
            "azure_openai",
            failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy,
            tags: new[] { "azure", "ai" },
            timeout: TimeSpan.FromSeconds(10));

        // Azure Document Intelligence health check
        healthChecksBuilder.AddCheck<MotorcycleRAG.Persistence.HealthChecks.DocumentIntelligenceHealthCheck>(
            "azure_document_intelligence",
            failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy,
            tags: new[] { "azure", "document" },
            timeout: TimeSpan.FromSeconds(5));

        // SQL Database health check
        healthChecksBuilder.AddCheck<MotorcycleRAG.Persistence.HealthChecks.SqlDatabaseHealthCheck>(
            "sql_database",
            failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy,
            tags: new[] { "database", "sql" },
            timeout: TimeSpan.FromSeconds(5));

        // Azure AI Foundry health check
        healthChecksBuilder.AddCheck<MotorcycleRAG.Persistence.HealthChecks.AzureFoundryHealthCheck>(
            "azure_foundry",
            failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded,
            tags: new[] { "azure", "foundry" },
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
    public static IServiceCollection AddSqlPersistence(this IServiceCollection services, IConfiguration configuration) {
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
    public static IServiceCollection AddWebTrustPolicyServices(this IServiceCollection services, IConfiguration configuration) {
        // Register web trust policy store as singleton
        services.AddSingleton<IWebTrustPolicyStore, MotorcycleRAG.Persistence.Configuration.WebTrustPolicyStore>();

        return services;
    }

    /// <summary>
    /// Validate overall configuration health
    /// </summary>
    private static Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult ValidateConfiguration(IConfiguration configuration) {
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

/// <summary>
/// Validator for SQL configuration options
/// Note: Connection string must be provided via SQL_CONNECTION_STRING environment variable
/// </summary>
internal class SqlOptionsValidator : IValidateOptions<MotorcycleRAG.Core.Options.SqlOptions> {
    public ValidateOptionsResult Validate(string? name, MotorcycleRAG.Core.Options.SqlOptions options) {
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

/// <summary>
/// Validator for Azure AI configuration
/// </summary>
internal class AzureAIConfigurationValidator : IValidateOptions<AzureAIOptions> {
    public ValidateOptionsResult Validate(string? name, AzureAIOptions options) {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.FoundryEndpoint))
            failures.Add("AzureAI:FoundryEndpoint is required");
        else if (!Uri.TryCreate(options.FoundryEndpoint, UriKind.Absolute, out _))
            failures.Add("AzureAI:FoundryEndpoint must be a valid URL");

        if (string.IsNullOrWhiteSpace(options.OpenAIEndpoint))
            failures.Add("AzureAI:OpenAIEndpoint is required");
        else if (!Uri.TryCreate(options.OpenAIEndpoint, UriKind.Absolute, out _))
            failures.Add("AzureAI:OpenAIEndpoint must be a valid URL");

        if (string.IsNullOrWhiteSpace(options.SearchServiceEndpoint))
            failures.Add("AzureAI:SearchServiceEndpoint is required");
        else if (!Uri.TryCreate(options.SearchServiceEndpoint, UriKind.Absolute, out _))
            failures.Add("AzureAI:SearchServiceEndpoint must be a valid URL");

        if (string.IsNullOrWhiteSpace(options.DocumentIntelligenceEndpoint))
            failures.Add("AzureAI:DocumentIntelligenceEndpoint is required");
        else if (!Uri.TryCreate(options.DocumentIntelligenceEndpoint, UriKind.Absolute, out _))
            failures.Add("AzureAI:DocumentIntelligenceEndpoint must be a valid URL");

        if (options.Models == null)
            failures.Add("AzureAI:Models configuration is required");
        else {
            if (string.IsNullOrWhiteSpace(options.Models.ChatModel))
                failures.Add("AzureAI:Models:ChatModel is required");
            if (string.IsNullOrWhiteSpace(options.Models.EmbeddingModel))
                failures.Add("AzureAI:Models:EmbeddingModel is required");
            if (options.Models.MaxTokens <= 0)
                failures.Add("AzureAI:Models:MaxTokens must be greater than 0");
            if (options.Models.Temperature < 0 || options.Models.Temperature > 2)
                failures.Add("AzureAI:Models:Temperature must be between 0 and 2");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}

/// <summary>
/// Validator for Search configuration
/// </summary>
internal class SearchConfigurationValidator : IValidateOptions<SearchOptions> {
    public ValidateOptionsResult Validate(string? name, SearchOptions options) {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.IndexName))
            failures.Add("Search:IndexName is required");

        if (options.BatchSize <= 0 || options.BatchSize > 1000)
            failures.Add("Search:BatchSize must be between 1 and 1000");

        if (options.MaxSearchResults <= 0 || options.MaxSearchResults > 100)
            failures.Add("Search:MaxSearchResults must be between 1 and 100");

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}

/// <summary>
/// Validator for Telemetry configuration
/// </summary>
internal class TelemetryConfigurationValidator : IValidateOptions<TelemetryOptions> {
    public ValidateOptionsResult Validate(string? name, TelemetryOptions options) {
        var failures = new List<string>();

        if (options.EnableTelemetry && string.IsNullOrWhiteSpace(options.ConnectionString))
            failures.Add("ApplicationInsights:ConnectionString is required when telemetry is enabled");

        if (string.IsNullOrWhiteSpace(options.ApplicationName))
            failures.Add("ApplicationInsights:ApplicationName is required");

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
