using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Identity.Web;
using Moq;
using MotorcycleRAG.API;
using MotorcycleRAG.API.Services;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.IntegrationTests;

/// <summary>
/// Custom WebApplicationFactory for integration tests
/// Provides test-specific configuration including dummy AzureAd settings
/// and adds test authentication handler for simulating authenticated users
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "xUnit fixture must be public")]
public class TestWebApplicationFactory : WebApplicationFactory<Program> {
    static TestWebApplicationFactory() {
        // Set environment to Testing early so Program.Main loads correct appsettings
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        // Program.cs enforces env vars only for Azure AD + Azure AI endpoints.
        // These are dummy test values (NOT secrets).
        Environment.SetEnvironmentVariable("MCR_API_AZURE_AD_TENANT_ID", "00000000-0000-0000-0000-000000000000");
        Environment.SetEnvironmentVariable("MCR_API_AZURE_AD_CLIENT_ID", "11111111-1111-1111-1111-111111111111");

        Environment.SetEnvironmentVariable("MCR_API_AZURE_OPENAI_ENDPOINT", "https://test-openai.openai.azure.com/");
        Environment.SetEnvironmentVariable("MCR_API_AZURE_SEARCH_ENDPOINT", "https://test-search.search.windows.net/");
        Environment.SetEnvironmentVariable("MCR_API_AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT", "https://test-docint.cognitiveservices.azure.com/");
        Environment.SetEnvironmentVariable("MCR_API_AZURE_FOUNDRY_ENDPOINT", "https://test-foundry.azure.com/");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) {
        ArgumentNullException.ThrowIfNull(builder);

        // Ensure SQL connection string requirement does not crash app startup in tests.
        // This is NOT a secret and MUST NOT include embedded credentials.
        Environment.SetEnvironmentVariable(
            "SQL_CONNECTION_STRING",
            "Server=(localdb)\\MSSQLLocalDB;Database=MotorcycleRAG_Test;Authentication=Active Directory Integrated;");

        Environment.SetEnvironmentVariable(
            "MCR_API_SQL_CONNECTION_STRING",
            "Server=(localdb)\\MSSQLLocalDB;Database=MotorcycleRAG_Test;Authentication=Active Directory Integrated;");

        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, config) => {
            // Add in-memory configuration with dummy AzureAd settings
            // These are NOT secrets - they're dummy values for test purposes only
            config.AddInMemoryCollection(new Dictionary<string, string?> {
                // AzureAd configuration - dummy values for testing (NOT secrets)
                ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
                ["AzureAd:Domain"] = "testdomain.onmicrosoft.com",
                ["AzureAd:TenantId"] = "00000000-0000-0000-0000-000000000000",
                ["AzureAd:ClientId"] = "11111111-1111-1111-1111-111111111111",
                ["AzureAd:CallbackPath"] = "/signin-oidc",
                ["AzureAd:SignedOutCallbackPath"] = "/signout-callback-oidc",
                ["AzureAd:ClientSecret"] = "test-client-secret-not-a-real-secret",

                // Ensure AppConfig is disabled for tests
                ["AppConfig:Endpoint"] = string.Empty,

                // Disable Application Insights for tests
                ["ApplicationInsights:ConnectionString"] = "InstrumentationKey=test-key",
                ["ApplicationInsights:EnableTelemetry"] = "false"
            });
        });

        builder.ConfigureServices(services => {
            // Replace production authentication with test authentication scheme
            // This allows tests to use X-Test-Auth header for authentication
            services.AddAuthentication(options => {
                options.DefaultAuthenticateScheme = "Test";
                options.DefaultChallengeScheme = "Test";
            })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", options => {
                    options.TimeProvider = TimeProvider.System;
                });

            // Ensure current user service exists by default (tests can override).
            services.AddHttpContextAccessor();
            services.AddScoped<ICurrentUserService, CurrentUserService>();

            // Provide safe default mocks for services that otherwise require SQL persistence.
            // Individual tests may override these with their own registrations.
            var userRepo = new Mock<IUserRepository>();
            userRepo.Setup(r => r.GetUserByIdAsync(It.IsAny<string>()))
                .ReturnsAsync((string userId) => new UserDTO {
                    Id = userId,
                    Email = "test@example.com",
                    DisplayName = "Test User",
                    FirstName = "Test",
                    LastName = "User",
                    IsEnabled = true,
                    CreatedDate = DateTime.UtcNow.AddDays(-1),
                    LastUpdatedDate = DateTime.UtcNow,
                    PlanId = "free-plan"
                });
            services.AddSingleton(userRepo.Object);

            var usageTracking = new Mock<IUsageTrackingService>();
            usageTracking.Setup(s => s.GetUsageByDateRangeAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
                .ReturnsAsync((string userId, DateTime start, DateTime end) => {
                    var days = Math.Max(1, (int)Math.Floor((end - start).TotalDays));
                    return Enumerable.Range(1, days).Select(i => new Usage {
                        Id = i,
                        UserId = userId,
                        Endpoint = "/api/me/usage",
                        HttpMethod = "GET",
                        QueryId = string.Empty,
                        RequestTime = start.AddDays(i - 1),
                        DurationMs = 10,
                        StatusCode = 200,
                        IsSuccess = true
                    }).ToArray();
                });
            usageTracking.Setup(s => s.RecordSuccessAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<string?>(), It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<string?>()))
                .ReturnsAsync(new Usage { Id = 1, IsSuccess = true, StatusCode = 200 });
            usageTracking.Setup(s => s.RecordFailureAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                    It.IsAny<string?>(), It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<string?>()))
                .ReturnsAsync(new Usage { Id = 1, IsSuccess = false, StatusCode = 500 });
            services.AddSingleton(usageTracking.Object);

            var planPolicy = new Mock<IPlanPolicyService>();
            planPolicy.Setup(s => s.HasExceededDailyLimitAsync(It.IsAny<string>(), It.IsAny<DateTime?>())).ReturnsAsync(false);
            planPolicy.Setup(s => s.GetDailyUsageCountAsync(It.IsAny<string>(), It.IsAny<DateTime?>())).ReturnsAsync(0);
            planPolicy.Setup(s => s.GetRemainingDailyRequestsAsync(It.IsAny<string>(), It.IsAny<DateTime?>())).ReturnsAsync(1000);
            planPolicy.Setup(s => s.GetDailyRequestLimitAsync(It.IsAny<UserDTO>())).ReturnsAsync(1000);
            services.AddSingleton(planPolicy.Object);

            var planRepo = new Mock<IPlanRepository>();
            planRepo.Setup(r => r.GetAllPlansAsync()).ReturnsAsync(Array.Empty<UserPlan>());
            planRepo.Setup(r => r.GetPlanByIdAsync(It.IsAny<string>())).ReturnsAsync((UserPlan?)null);
            planRepo.Setup(r => r.CreatePlanAsync(It.IsAny<UserPlan>())).ReturnsAsync((UserPlan p) => p);
            planRepo.Setup(r => r.UpdatePlanAsync(It.IsAny<UserPlan>())).ReturnsAsync(true);
            planRepo.Setup(r => r.DeletePlanAsync(It.IsAny<string>())).ReturnsAsync(true);
            services.AddSingleton(planRepo.Object);

            var userAdmin = new Mock<IUserAdminService>();
            userAdmin.Setup(s => s.SetUserEnabledStatusAsync(It.IsAny<string>(), It.IsAny<bool>()))
                .ReturnsAsync((string userId, bool enabled) => new UserDTO { Id = userId, IsEnabled = enabled, Email = "test@example.com" });
            userAdmin.Setup(s => s.AssignPlanToUserAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((string userId, string planId) => new UserDTO { Id = userId, PlanId = planId, Email = "test@example.com" });
            userAdmin.Setup(s => s.GetAllUsersAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(Array.Empty<UserDTO>());
            services.AddSingleton(userAdmin.Object);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder) {
        // Set environment to Testing
        builder.UseEnvironment("Testing");

        return base.CreateHost(builder);
    }
}
