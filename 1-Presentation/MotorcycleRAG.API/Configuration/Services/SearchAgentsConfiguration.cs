using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Application.Agents.Orchestration;
using MotorcycleRAG.Application.Services.Telemetry;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Application.Services.TrustedSources;
using MotorcycleRAG.Persistence.Azure;

namespace MotorcycleRAG.API.Configuration.Services;

/// <summary>
/// Configuration for search agents
/// </summary>
internal static class SearchAgentsConfiguration
{
    /// <summary>
    /// Configure search agents
    /// </summary>
    internal static IServiceCollection AddSearchAgents(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure search options
        services.Configure<SearchOptions>(configuration.GetSection("Search"));

        // Register trusted sources loader (loads WebSource from DB for WebSearchAgent sub-agent)
        services.AddScoped<ITrustedSourcesLoader, DatabaseTrustedSourcesLoader>();

        // Register Foundry tool dispatcher and handlers (scoped: one set per HTTP request)
        services.AddScoped<FoundryToolDispatcher>();

        services.AddScoped<SubAgentToolHandlers>();

        services.AddScoped<DegradedModeTracker>();

        services.AddScoped<OrchestratorToolHandlers>(sp =>
        {
            var runner = sp.GetRequiredService<IFoundryAgentRunner>();
            var subAgentDispatcher = new FoundryToolDispatcher();
            var subAgentHandlers = sp.GetRequiredService<SubAgentToolHandlers>();
            subAgentHandlers.RegisterOn(subAgentDispatcher);

            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AzureFoundryOptions>>();
            var logger = sp.GetRequiredService<ILogger<OrchestratorToolHandlers>>();
            var degradedModeTracker = sp.GetRequiredService<DegradedModeTracker>();
            return new OrchestratorToolHandlers(runner, subAgentDispatcher, options, logger, degradedModeTracker);
        });

        // Wire up the main orchestrator dispatcher with orchestrator tool handlers
        services.AddScoped<MotorcycleRAG.Application.Services.AgentOrchestrator>(sp =>
        {
            var agents = sp.GetServices<ISearchAgent>();
            var logger = sp.GetRequiredService<ILogger<MotorcycleRAG.Application.Services.AgentOrchestrator>>();
            var runner = sp.GetRequiredService<IFoundryAgentRunner>();
            var orchestratorHandlers = sp.GetRequiredService<OrchestratorToolHandlers>();

            var mainDispatcher = sp.GetRequiredService<FoundryToolDispatcher>();
            orchestratorHandlers.RegisterOn(mainDispatcher);

            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AzureFoundryOptions>>();
            var correlationService = sp.GetRequiredService<ICorrelationService>();
            return new MotorcycleRAG.Application.Services.AgentOrchestrator(agents, logger, runner, mainDispatcher, options, correlationService);
        });

        services.AddScoped<IAgentOrchestrator>(sp =>
            sp.GetRequiredService<MotorcycleRAG.Application.Services.AgentOrchestrator>());

        return services;
    }
}

