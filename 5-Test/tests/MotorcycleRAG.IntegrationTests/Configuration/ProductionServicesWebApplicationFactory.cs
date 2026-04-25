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
    static ProductionServicesWebApplicationFactory() {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("MCR_API_AZURE_AD_TENANT_ID", "00000000-0000-0000-0000-000000000000");
        Environment.SetEnvironmentVariable("MCR_API_AZURE_AD_CLIENT_ID", "11111111-1111-1111-1111-111111111111");
        Environment.SetEnvironmentVariable("MCR_API_AZURE_OPENAI_ENDPOINT", "https://test-openai.openai.azure.com/");
        Environment.SetEnvironmentVariable("MCR_API_AZURE_SEARCH_ENDPOINT", "https://test-search.search.windows.net/");
        Environment.SetEnvironmentVariable("MCR_API_AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT", "https://test-docint.cognitiveservices.azure.com/");
        Environment.SetEnvironmentVariable("MCR_API_AZURE_FOUNDRY_ENDPOINT", "https://test-foundry.azure.com/");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) {
        ArgumentNullException.ThrowIfNull(builder);

        Environment.SetEnvironmentVariable(
            "SQL_CONNECTION_STRING",
            "Server=(localdb)\\MSSQLLocalDB;Database=MotorcycleRAG_Test;Authentication=Active Directory Integrated;");

        Environment.SetEnvironmentVariable(
            "MCR_API_SQL_CONNECTION_STRING",
            "Server=(localdb)\\MSSQLLocalDB;Database=MotorcycleRAG_Test;Authentication=Active Directory Integrated;");

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
                ["AppConfig:Endpoint"] = string.Empty,
                ["ApplicationInsights:ConnectionString"] = "InstrumentationKey=test-key",
                ["ApplicationInsights:EnableTelemetry"] = "false"
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
