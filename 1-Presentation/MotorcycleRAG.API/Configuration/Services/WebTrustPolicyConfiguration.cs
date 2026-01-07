using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;

namespace MotorcycleRAG.API.Configuration.Services;

/// <summary>
/// Configuration for web trust policy services
/// </summary>
internal static class WebTrustPolicyConfiguration
{
    /// <summary>
    /// Configure web trust policy services
    /// </summary>
    internal static IServiceCollection AddWebTrustPolicyServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Register web trust policy store as singleton
        services.AddSingleton<IWebTrustPolicyStore, MotorcycleRAG.Persistence.Configuration.WebTrustPolicyStore>();

        return services;
    }
}
