using System.Collections.Generic;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using MotorcycleRAG.API;
using MotorcycleRAG.API.Services;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.IntegrationTests.Configuration;

/// <summary>
/// WebApplicationFactory that starts the API with production DI registrations intact.
/// Only fakes auth and external infrastructure credentials (SQL, Azure AI endpoints).
/// Does NOT register mocks for core application services (IUsageTrackingService, IPlanPolicyService, etc.)
/// so that missing production registrations are caught at test time rather than in production.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "xUnit fixture must be public")]
public class ProductionServicesWebApplicationFactory : WebApplicationFactory<Program> {
    protected override void ConfigureWebHost(IWebHostBuilder builder) {
        ArgumentNullException.ThrowIfNull(builder);

        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, config) => {
            config.AddInMemoryCollection(new Dictionary<string, string?> {
                ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
                ["AzureAd:Domain"] = "testdomain.onmicrosoft.com",
                ["AzureAd:TenantId"] = "00000000-0000-0000-0000-000000000000",
                ["AzureAd:ClientId"] = "11111111-1111-1111-1111-111111111111",
                ["AzureAd:CallbackPath"] = "/signin-oidc",
                ["AzureAd:SignedOutCallbackPath"] = "/signout-callback-oidc",
                ["AzureAd:ClientSecret"] = "test-client-secret-not-a-real-secret",
                ["AzureAd:AdminClientId"] = "11111111-1111-1111-1111-111111111111",
                ["AzureAd:LocalProcessorClientId"] = "22222222-2222-2222-2222-222222222222",
                ["Authentication:Audience"] = "11111111-1111-1111-1111-111111111111",
                ["Authentication:Issuers:Workforce"] = "https://login.microsoftonline.com/00000000-0000-0000-0000-000000000000/v2.0",
                ["AppConfig:Endpoint"] = string.Empty,
                ["ApplicationInsights:ConnectionString"] = "InstrumentationKey=test-key",
                ["ApplicationInsights:EnableTelemetry"] = "false",
                ["AzureAI:OpenAIEndpoint"] = "https://test-openai.openai.azure.com/",
                ["AzureAI:SearchServiceEndpoint"] = "https://test-search.search.windows.net/",
                ["AzureAI:DocumentIntelligenceEndpoint"] = "https://test-docint.cognitiveservices.azure.com/",
                ["AzureAI:FoundryEndpoint"] = "https://test-foundry.services.ai.azure.com/",
                ["Sql:ConnectionString"] = "Server=(localdb)\\MSSQLLocalDB;Database=MotorcycleRAG_Test;Authentication=Active Directory Integrated;",
                ["Onboarding:ApproverAddress"] = "approver@example.invalid",
                ["ExternalIdentityProvisioning:InviteRedirectUrl"] = "https://localhost/signin-oidc",
                ["ExternalIdentityProvisioning:ApiApplicationClientId"] = "33333333-3333-3333-3333-333333333333"
            });
        });

        builder.ConfigureServices(services => {
            // Replace auth only — all production service registrations remain untouched.
            services.AddAuthentication(options => {
                options.DefaultAuthenticateScheme = "Test";
                options.DefaultChallengeScheme = "Test";
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", options => {
                options.TimeProvider = TimeProvider.System;
            });

            services.AddHttpContextAccessor();
            services.AddScoped<ICurrentUserService, CurrentUserService>();

            var userProvisioning = new Mock<IUserProvisioningService>();
            userProvisioning
                .Setup(service => service.ResolveManagedUserIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IdentityProvider>()))
                .ReturnsAsync((string?)null);
            userProvisioning
                .Setup(service => service.GetApprovedManagedUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IdentityProvider>()))
                .ReturnsAsync((UserDTO?)null);
            userProvisioning
                .Setup(service => service.ReconcileApprovedUserAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<IdentityProvider>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>()))
                .ReturnsAsync((string issuer, string subject, string email, string? displayName, string? firstName, string? lastName, IdentityProvider provider, string? providerUserId, string? objectId) => new UserDTO {
                    Id = string.IsNullOrWhiteSpace(subject) ? email : subject,
                    Email = email,
                    DisplayName = displayName ?? email,
                    FirstName = firstName ?? string.Empty,
                    LastName = lastName ?? string.Empty,
                    IsEnabled = true,
                    AccessState = ManagedUserAccessState.Active,
                    PlanId = "free-plan",
                    AuthProvider = provider.ToString(),
                    ProviderUserId = providerUserId ?? subject
                });
            services.AddSingleton(userProvisioning.Object);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder) {
        builder.UseEnvironment("Testing");
        return base.CreateHost(builder);
    }
}
