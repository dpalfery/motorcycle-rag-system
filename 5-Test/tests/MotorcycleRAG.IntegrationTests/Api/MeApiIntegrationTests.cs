using System;
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
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models;
using Xunit;

namespace MotorcycleRAG.IntegrationTests.Api
{
    /// <summary>
    /// Integration tests for /api/me endpoints
    /// </summary>
    public class MeApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;

        public MeApiIntegrationTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task GetProfile_Unauthenticated_ReturnsUnauthorized()
        {
            // Act
            var response = await _client.GetAsync("/api/me");

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task GetProfile_Authenticated_ReturnsUserProfile()
        {
            // Arrange
            var mockUserService = new Mock<ICurrentUserService>();
            mockUserService.Setup(s => s.UserId).Returns("test-user-1");
            mockUserService.Setup(s => s.Email).Returns("test@example.com");
            mockUserService.Setup(s => s.DisplayName).Returns("Test User");
            mockUserService.Setup(s => s.FirstName).Returns("Test");
            mockUserService.Setup(s => s.LastName).Returns("User");
            mockUserService.Setup(s => s.IsAuthenticated).Returns(true);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton(mockUserService.Object);
                });
            }).CreateClient();

            // Act
            var response = await client.GetAsync("/api/me");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var userProfile = System.Text.Json.JsonSerializer.Deserialize<UserProfileResponse>(content, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

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
        public async Task GetUsage_Unauthenticated_ReturnsUnauthorized()
        {
            // Act
            var response = await _client.GetAsync("/api/me/usage");

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task GetUsage_Authenticated_ReturnsUsageInformation()
        {
            // Arrange
            var mockUserService = new Mock<ICurrentUserService>();
            mockUserService.Setup(s => s.UserId).Returns("test-user-1");
            mockUserService.Setup(s => s.IsAuthenticated).Returns(true);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton(mockUserService.Object);
                });
            }).CreateClient();

            // Act
            var response = await client.GetAsync("/api/me/usage?days=7");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var usageResponse = System.Text.Json.JsonSerializer.Deserialize<UsageResponse>(content, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(usageResponse);
            Assert.Equal("test-user-1", usageResponse?.UserId);
            Assert.Equal(7, usageResponse?.TotalRequests);
            Assert.Equal(7, usageResponse?.UsageRecords?.Length);
        }

        [Fact]
        public async Task GetUsage_InvalidDays_ClampsToValidRange()
        {
            // Arrange
            var mockUserService = new Mock<ICurrentUserService>();
            mockUserService.Setup(s => s.UserId).Returns("test-user-1");
            mockUserService.Setup(s => s.IsAuthenticated).Returns(true);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton(mockUserService.Object);
                });
            }).CreateClient();

            // Act - test with days > 30
            var response = await client.GetAsync("/api/me/usage?days=50");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var usageResponse = System.Text.Json.JsonSerializer.Deserialize<UsageResponse>(content, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            // Assert - should clamp to 30
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(usageResponse);
        }

        [Fact]
        public async Task Query_WithExceededLimit_Returns429()
        {
            // Arrange
            var mockUserService = new Mock<ICurrentUserService>();
            mockUserService.Setup(s => s.UserId).Returns("test-user-1");
            mockUserService.Setup(s => s.IsAuthenticated).Returns(true);

            var mockPlanPolicyService = new Mock<IPlanPolicyService>();
            mockPlanPolicyService
                .Setup(s => s.HasExceededDailyLimitAsync("test-user-1", It.IsAny<DateTime>()))
                .ReturnsAsync(true);
            mockPlanPolicyService
                .Setup(s => s.GetRemainingDailyRequestsAsync("test-user-1", It.IsAny<DateTime>()))
                .ReturnsAsync(0);

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

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton(mockUserService.Object);
                    services.AddSingleton(mockPlanPolicyService.Object);
                    services.AddSingleton(mockUsageTrackingService.Object);
                });
            }).CreateClient();

            // Act
            var response = await client.PostAsync("/api/motorcycles/query", 
                new StringContent(
                    """{"query": "test query"}""",
                    Encoding.UTF8,
                    "application/json"));

            // Assert
            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);

            var content = await response.Content.ReadAsStringAsync();
            Assert.Contains("Daily request limit exceeded", content);
            Assert.Contains("remainingRequests", content);
        }

        [Fact]
        public async Task Query_WithinLimit_AllowsRequest()
        {
            // Arrange
            var mockUserService = new Mock<ICurrentUserService>();
            mockUserService.Setup(s => s.UserId).Returns("test-user-1");
            mockUserService.Setup(s => s.IsAuthenticated).Returns(true);

            var mockPlanPolicyService = new Mock<IPlanPolicyService>();
            mockPlanPolicyService
                .Setup(s => s.HasExceededDailyLimitAsync("test-user-1", It.IsAny<DateTime>()))
                .ReturnsAsync(false);
            mockPlanPolicyService
                .Setup(s => s.GetRemainingDailyRequestsAsync("test-user-1", It.IsAny<DateTime>()))
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

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton(mockUserService.Object);
                    services.AddSingleton(mockPlanPolicyService.Object);
                    services.AddSingleton(mockUsageTrackingService.Object);
                });
            }).CreateClient();

            // Act
            var response = await client.PostAsync("/api/motorcycles/query", 
                new StringContent(
                    """{"query": "test query"}""",
                    Encoding.UTF8,
                    "application/json"));

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
