using Microsoft.Extensions.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Application.Services.Citations;
using MotorcycleRAG.Application.Services.QueryValidation;
using MotorcycleRAG.Application.Services.QueryProcessing;
using MotorcycleRAG.Application.Services.ResponseProcessing;
using MotorcycleRAG.Application.Services.Metrics;

namespace MotorcycleRAG.API.Configuration.Services;

/// <summary>
/// Configuration for core application services
/// </summary>
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
internal static class CoreServicesConfiguration {
    /// <summary>
    /// Configure core application services
    /// </summary>
    internal static IServiceCollection AddCoreServices(this IServiceCollection services) {
        // Register core service interfaces to concrete implementations in Application layer
        services.AddScoped<IMotorcycleRagService, MotorcycleRAG.Application.Services.MotorcycleRagService>();
        // IAgentOrchestrator is registered in SearchAgentsConfiguration with full Foundry dispatcher wiring

        // Register extracted services for MotorcycleRagService
        services.AddScoped<ClaimCitationService>();
        services.AddScoped<QueryRefinementService>();
        services.AddScoped<QuestionValidationService>();
        services.AddScoped<QuestionValidationState>();
        services.AddScoped<ResponseLimitationAnalyzer>();
        services.AddScoped<QueryCostCalculator>();

        services.AddScoped<MotorcycleRagServiceDependencies>();

        services.AddScoped<IUsageTrackingService, UsageTrackingService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<ICurrentUserProfileService, CurrentUserProfileService>();
        services.AddScoped<IPlanAdministrationService, PlanAdministrationService>();
        services.AddScoped<IPlanPolicyService, PlanPolicyService>();
        services.AddScoped<IUserAdminService, UserAdminService>();
        services.AddScoped<IUserProvisioningService, UserProvisioningService>();
        services.AddScoped<AccessRequestService>();
        services.AddScoped<AccessRequestAdminService>();
        services.AddScoped<ApprovalOnboardingService>();
        services.AddScoped<TierEntitlementMappingService>();
        services.AddScoped<UserAccessLifecycleService>();
        services.AddScoped<WebSourceRegistryService>();
        services.AddScoped<IWebScrapeOrchestrator, WebScrapeOrchestrator>();
        services.AddScoped<IToolConfigurationService, ToolConfigurationService>();
        services.AddScoped<IMcpConfigurationProvider, McpConfigurationProvider>();

        // D7 motorcycle category classifier (Application service). Infrastructure dependencies
        // (IBikeModelCategoryRepository, ILocalChatClient, ClassifierOptions) are registered in
        // Persistence (AddSqlPersistenceServices / AddClassifierServices). Registered against the
        // IMotorcycleCategoryClassifier contract so the Persistence-layer ChunkIndexingService can
        // consume category resolution without depending on Application (Dependency Rule).
        services.AddScoped<IMotorcycleCategoryClassifier, MotorcycleCategoryClassifier>();
        services.AddScoped<MotorcycleCategoryClassifier>();

        return services;
    }
}
