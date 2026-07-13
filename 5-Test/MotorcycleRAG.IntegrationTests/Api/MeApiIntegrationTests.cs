using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MotorcycleRAG.API;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Domain.Entities;
using Xunit;
using MotorcycleRAG.IntegrationTests;


namespace MotorcycleRAG.IntegrationTests.Api {
    /// <summary>
    /// Integration tests for /api/me endpoints
    /// </summary>
    public class MeApiIntegrationTests : IClassFixture<TestWebApplicationFactory> {
        private readonly TestWebApplicationFactory _factory;
        private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        public MeApiIntegrationTests(TestWebApplicationFactory factory) {
            _factory = factory;
        }

        [Fact]
        public async Task GetProfile_Unauthenticated_ReturnsUnauthorized() {
            // Act
            var client = _factory.CreateClient();

            var response = await client.GetAsync("/api/me");

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task GetProfile_Authenticated_ReturnsUserProfile() {
            // Arrange
            var mockUserService = new Mock<ICurrentUserService>();
            mockUserService.Setup(s => s.UserId).Returns("test-user-1");
            mockUserService.Setup(s => s.Email).Returns("test@example.com");
            mockUserService.Setup(s => s.DisplayName).Returns("Test User");
            mockUserService.Setup(s => s.FirstName).Returns("Test");
            mockUserService.Setup(s => s.LastName).Returns("User");
            mockUserService.Setup(s => s.IsAuthenticated).Returns(true);
            mockUserService.Setup(s => s.GetManagedUserAsync()).ReturnsAsync(new UserDTO {
                Id = "test-user-1",
                Email = "test@example.com",
                DisplayName = "Test User",
                FirstName = "Test",
                LastName = "User",
                IsEnabled = true,
                AccessState = ManagedUserAccessState.Active,
                PlanId = "free-plan"
            });

            using var factory = _factory.WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.AddSingleton(mockUserService.Object);
                });
            });
            using var client = factory.CreateClientWithRoles("User");

            // Act
            var response = await client.GetAsync("/api/me");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var userProfile = System.Text.Json.JsonSerializer.Deserialize<UserProfileResponse>(content, JsonOptions);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(userProfile);
            Assert.Equal("test-user-1", userProfile?.Id);
            Assert.Equal("test@example.com", userProfile?.Email);
            Assert.Equal("Test User", userProfile?.DisplayName);
            Assert.Equal("Test", userProfile?.FirstName);
            Assert.Equal("User", userProfile?.LastName);
        }

        [Fact]
        public async Task GetUsage_Unauthenticated_ReturnsUnauthorized() {
            // Act
            var client = _factory.CreateClient();

            var response = await client.GetAsync("/api/me/usage");

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task GetUsage_Authenticated_ReturnsUsageInformation() {
            // Arrange
            var mockUserService = new Mock<ICurrentUserService>();
            mockUserService.Setup(s => s.UserId).Returns("test-user-1");
            mockUserService.Setup(s => s.IsAuthenticated).Returns(true);
            mockUserService.Setup(s => s.GetManagedUserAsync()).ReturnsAsync(new UserDTO {
                Id = "test-user-1",
                Email = "test@example.com",
                IsEnabled = true,
                AccessState = ManagedUserAccessState.Active,
                PlanId = "free-plan"
            });

            using var factory = _factory.WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.AddSingleton(mockUserService.Object);
                });
            });
            using var client = factory.CreateClientWithRoles("User");

            // Act
            var response = await client.GetAsync("/api/me/usage?days=7");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var usageResponse = System.Text.Json.JsonSerializer.Deserialize<UsageResponse>(content, JsonOptions);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(usageResponse);
            Assert.Equal("test-user-1", usageResponse?.UserId);
            Assert.Equal(7, usageResponse?.TotalRequests);
            Assert.Equal(7, usageResponse?.UsageRecords?.Length);
        }

        [Fact]
        public async Task GetUsage_InvalidDays_ClampsToValidRange() {
            // Arrange
            var mockUserService = new Mock<ICurrentUserService>();
            mockUserService.Setup(s => s.UserId).Returns("test-user-1");
            mockUserService.Setup(s => s.IsAuthenticated).Returns(true);
            mockUserService.Setup(s => s.GetManagedUserAsync()).ReturnsAsync(new UserDTO {
                Id = "test-user-1",
                Email = "test@example.com",
                IsEnabled = true,
                AccessState = ManagedUserAccessState.Active,
                PlanId = "free-plan"
            });

            using var factory = _factory.WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.AddSingleton(mockUserService.Object);
                });
            });
            using var client = factory.CreateClientWithRoles("User");

            // Act - test with days > 30
            var response = await client.GetAsync("/api/me/usage?days=50");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var usageResponse = System.Text.Json.JsonSerializer.Deserialize<UsageResponse>(content, JsonOptions);

            // Assert - should clamp to 30
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(usageResponse);
        }

        [Fact]
        public async Task Query_WithExceededLimit_Returns429() {
            // Arrange
            var mockUserService = new Mock<ICurrentUserService>();
            mockUserService.Setup(s => s.UserId).Returns("test-user-1");
            mockUserService.Setup(s => s.IsAuthenticated).Returns(true);
            mockUserService.Setup(s => s.GetManagedUserIdAsync()).ReturnsAsync("test-user-1");

            var mockPlanPolicyService = new Mock<IPlanPolicyService>();
            mockPlanPolicyService
                .Setup(s => s.HasExceededDailyLimitAsync("test-user-1", It.IsAny<DateTime?>()))
                .ReturnsAsync(true);
            mockPlanPolicyService
                .Setup(s => s.GetRemainingDailyRequestsAsync("test-user-1", It.IsAny<DateTime?>()))
                .ReturnsAsync(0);

            var mockUsageTrackingService = new Mock<IUsageTrackingService>();
            mockUsageTrackingService
                .Setup(s => s.RecordFailureAsync(
                    "test-user-1",
                    "/api/motorcycles/query",
                    "POST",
                    429,
                    It.IsAny<string>(),
                    It.IsAny<long>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()))
                .ReturnsAsync(new Usage { Id = 1 });

            var mockMcpProvider = new Mock<IMcpConfigurationProvider>();
            mockMcpProvider.Setup(m => m.GetEnabledToolsAsync()).ReturnsAsync(Array.Empty<McpToolConfiguration>());

            using var factory = _factory.WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.AddSingleton(mockUserService.Object);
                    services.AddSingleton(mockPlanPolicyService.Object);
                    services.AddSingleton(mockUsageTrackingService.Object);
                    services.AddSingleton(mockMcpProvider.Object);
                });
            });
            using var client = factory.CreateClientWithRoles("User");

            // Act
            using var content = new StringContent(
                """{"Query": "test query"}""",
                Encoding.UTF8,
                "application/json");
            var response = await client.PostAsync("/api/motorcycles/query", content);

            // Assert
            if (response.StatusCode != HttpStatusCode.TooManyRequests) {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Xunit.Sdk.XunitException($"Expected TooManyRequests but got {response.StatusCode}. Response: {errorContent}");
            }
            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);

            var responseContent = await response.Content.ReadAsStringAsync();
            Assert.Contains("Daily request limit exceeded", responseContent);
            Assert.Contains("remainingRequests", responseContent);
        }

        [Fact]
        public async Task Query_WithinLimit_AllowsRequest() {
            // Arrange
            var mockUserService = new Mock<ICurrentUserService>();
            mockUserService.Setup(s => s.UserId).Returns("test-user-1");
            mockUserService.Setup(s => s.IsAuthenticated).Returns(true);
            mockUserService.Setup(s => s.GetManagedUserIdAsync()).ReturnsAsync("test-user-1");

            var mockPlanPolicyService = new Mock<IPlanPolicyService>();
            mockPlanPolicyService
                .Setup(s => s.HasExceededDailyLimitAsync("test-user-1", It.IsAny<DateTime?>()))
                .ReturnsAsync(false);
            mockPlanPolicyService
                .Setup(s => s.GetRemainingDailyRequestsAsync("test-user-1", It.IsAny<DateTime?>()))
                .ReturnsAsync(50);

            var mockUsageTrackingService = new Mock<IUsageTrackingService>();
            mockUsageTrackingService
                .Setup(s => s.RecordSuccessAsync(
                    "test-user-1",
                    "/api/motorcycles/query",
                    "POST",
                    It.IsAny<string>(),
                    It.IsAny<long>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()))
                .ReturnsAsync(new Usage { Id = 1 });

            var mockRagService = new Mock<IMotorcycleRagService>();
            mockRagService
                .Setup(s => s.QueryAsync(It.IsAny<MotorcycleQueryRequest>()))
                .ReturnsAsync(new MotorcycleQueryResponse {
                    QueryId = Guid.NewGuid().ToString(),
                    Response = "Test answer",
                    Sources = Array.Empty<SearchResult>(),
                    Metrics = new QueryMetrics { ProcessingTimeMs = 100 }
                });

            var mockMcpProvider = new Mock<IMcpConfigurationProvider>();
            mockMcpProvider.Setup(m => m.GetEnabledToolsAsync()).ReturnsAsync(Array.Empty<McpToolConfiguration>());

            using var factory = _factory.WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.AddSingleton(mockUserService.Object);
                    services.AddSingleton(mockPlanPolicyService.Object);
                    services.AddSingleton(mockUsageTrackingService.Object);
                    services.AddSingleton(mockRagService.Object);
                    services.AddSingleton(mockMcpProvider.Object);
                });
            });
            using var client = factory.CreateClientWithRoles("User");

            // Act
            using var content = new StringContent(
                """{"Query": "test query"}""",
                Encoding.UTF8,
                "application/json");
            var response = await client.PostAsync("/api/motorcycles/query", content);

            // Assert
            if (response.StatusCode != HttpStatusCode.OK) {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Xunit.Sdk.XunitException($"Expected OK but got {response.StatusCode}. Response: {errorContent}");
            }
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task GetProfile_WhenManagedIdentityResolutionFails_ReturnsForbidden() {
            var mockUserService = new Mock<ICurrentUserService>();
            mockUserService.Setup(s => s.IsAuthenticated).Returns(true);
            mockUserService.Setup(s => s.GetManagedUserAsync()).ReturnsAsync((UserDTO?)null);

            using var factory = _factory.WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.AddSingleton(mockUserService.Object);
                });
            });
            using var client = factory.CreateClientWithRoles("User");

            var response = await client.GetAsync("/api/me");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }
}
