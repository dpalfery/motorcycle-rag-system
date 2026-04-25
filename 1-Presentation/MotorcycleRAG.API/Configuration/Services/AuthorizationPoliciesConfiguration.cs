using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MotorcycleRAG.API.Configuration;
using MotorcycleRAG.API.Extensions;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.API.Configuration.Services;

/// <summary>
/// Configuration for authorization policies.
/// </summary>
internal static class AuthorizationPoliciesConfiguration {
    internal static IServiceCollection AddMotorcycleRagAuthorization(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment env) {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(env);
        // Get Admin Client ID from configuration for isolation checks
        var adminClientId = configuration["AzureAd:AdminClientId"];
        var localProcessorClientId = configuration["AzureAd:LocalProcessorClientId"];

        // Provide a dummy Client ID for testing environment if not set
        if (string.IsNullOrEmpty(adminClientId) && env.IsEnvironment("Testing")) {
            adminClientId = "11111111-1111-1111-1111-111111111111";
        }

        services.AddAuthorization(options => {
            // Admin policy - requires BOTH admin scope AND admin app role AND correct Client ID
            options.AddPolicy(AuthorizationPolicyNames.Admin, policy => {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(ctx => {
                    var hasScope = ctx.User.HasScope("admin");
                    var hasRole = ctx.User.HasAnyRole("mcr-api-admin");
                    var isAuthorizedClient = !string.IsNullOrWhiteSpace(adminClientId)
                        && ctx.User.IsAuthorizedClient(adminClientId);

                    // In testing, we enforce roles strictly but can be flexible with scope/client if headers are used instead of JWT
                    if (env.IsEnvironment("Testing")) {
                        return hasRole && (hasScope || ctx.User.HasClaim("X-Test-Auth", "mcr-api-admin"));
                    }

                    return hasScope && hasRole && isAuthorizedClient;
                });
            });

            // Local processor policy — M2M only, no delegated scope, checks File.Upload.All role + azp
            options.AddPolicy(AuthorizationPolicyNames.LocalProcessor, policy => {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(ctx => {
                    var hasRole = ctx.User.HasAnyRole("File.Upload.All");
                    var isAuthorizedClient = !string.IsNullOrWhiteSpace(localProcessorClientId)
                        && ctx.User.IsAuthorizedClient(localProcessorClientId);

                    if (env.IsEnvironment("Testing")) {
                        return hasRole || ctx.User.HasClaim("X-Test-Auth", "mcr-api-local-processor");
                    }

                    return hasRole && isAuthorizedClient;
                });
            });

            // Read policy - requires read scope
            options.AddPolicy(AuthorizationPolicyNames.Read, policy => {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(ctx => ctx.User.HasScope("read"));
            });

            // Chat policy - requires chat scope
            options.AddPolicy(AuthorizationPolicyNames.Chat, policy => {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(ctx => ctx.User.HasScope("chat"));
            });

            // User policy - requires User app role (no scope requirement for regular users)
            options.AddPolicy(AuthorizationPolicyNames.User, policy => {
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(System.Security.Claims.ClaimTypes.Role, "User");
            });

            // Viewer policy - requires Viewer app role (no scope requirement for read-only access)
            options.AddPolicy(AuthorizationPolicyNames.Viewer, policy => {
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(System.Security.Claims.ClaimTypes.Role, "Viewer");
            });

            // Manuals view policy - allows User, Viewer, and admin roles to view manual pages
            options.AddPolicy(AuthorizationPolicyNames.ManualsView, policy => {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(ctx =>
                    ctx.User.HasAnyRole("User", "Viewer", "mcr-api-admin"));
            });

            // Default policy - requires any authenticated user and defaults to denying anonymous access
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();

            options.DefaultPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        return services;
    }
}
