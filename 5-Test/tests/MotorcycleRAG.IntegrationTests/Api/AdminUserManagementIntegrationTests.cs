using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MotorcycleRAG.API;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Models;
using Xunit;

namespace MotorcycleRAG.IntegrationTests.Api
{
    /// <summary>
    /// Integration tests for admin user management and plan assignment
    /// </summary>
    public class AdminUserManagementIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public AdminUserManagementIntegrationTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task SetUserEnabledStatus_Unauthenticated_ReturnsUnauthorized()
        {
            // Act
            var client = _factory.CreateClient();
            var response = await client.PutAsync("/api/admin/users/user-1/enabled", 
                new StringContent(
                    """{"isEnabled": true}""",
                    Encoding.UTF8,
                    "application/json"));

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task SetUserEnabledStatus_WithoutAdminRole_ReturnsForbidden()
        {
            // Arrange
            var mockUserService = new Mock<ICurrentUserService>();
            mockUserService.Setup(s => s.UserId).Returns("test-user-1");
            mockUserService.Setup(s => s.IsAuthenticated).Returns(true);
            mockUserService.Setup(s => s.IsInRole("Admin")).Returns(false);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton(mockUserService.Object);
                });
            }).CreateClient();

            // Act
            var response = await client.PutAsync("/api/admin/users/user-1/enabled", 
                new StringContent(
                    """{"isEnabled": true}""",
                    Encoding.UTF8,
                    "application/json"));

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task SetUserEnabledStatus_WithAdminRole_Succeeds()
        {
            // Arrange
            var mockUserService = new Mock<ICurrentUserService>();
            mockUserService.Setup(s => s.UserId).Returns("test-user-1");
            mockUserService.Setup(s => s.IsAuthenticated).Returns(true);
            mockUserService.Setup(s => s.IsInRole("Admin")).Returns(true);

            var mockUserAdminService = new Mock<IUserAdminService>();
            mockUserAdminService
                .Setup(s => s.SetUserEnabledStatusAsync("test-user-1", true))
                .ReturnsAsync(new User 
                { 
                    Id = "test-user-1",
                    Email = "test@example.com",
                    IsEnabled = true
                });

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton(mockUserService.Object);
                    services.AddSingleton(mockUserAdminService.Object);
                });
            }).CreateClient();

            // Act
            var response = await client.PutAsync("/api/admin/users/test-user-1/enabled", 
                new StringContent(
                    """{"isEnabled": true}""",
                    Encoding.UTF8,
                    "application/json"));

            // Assert
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            var user = System.Text.Json.JsonSerializer.Deserialize<User>(content, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            Assert.NotNull(user);
            Assert.True(user?.IsEnabled);
        }

        [Fact]
        public async Task AssignPlanToUser_WithAdminRole_Succeeds()
        {
            // Arrange
            var mockUserService = new Mock<ICurrentUserService>();
            mockUserService.Setup(s => s.UserId).Returns("test-user-1");
            mockUserService.Setup(s => s.IsAuthenticated).Returns(true);
            mockUserService.Setup(s => s.IsInRole("Admin")).Returns(true);

            var mockUserAdminService = new Mock<IUserAdminService>();
            mockUserAdminService
                .Setup(s => s.AssignPlanToUserAsync("test-user-1", "premium-plan"))
                .ReturnsAsync(new User 
                { 
                    Id = "test-user-1",
                    Email = "test@example.com",
                    PlanId = "premium-plan"
                });

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton(mockUserService.Object);
                    services.AddSingleton(mockUserAdminService.Object);
                });
            }).CreateClient();

            // Act
            var response = await client.PutAsync("/api/admin/users/test-user-1/plan", 
                new StringContent(
                    """{"planId": "premium-plan"}""",
                    Encoding.UTF8,
                    "application/json"));

            // Assert
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            var user = System.Text.Json.JsonSerializer.Deserialize<User>(content, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            Assert.NotNull(user);
            Assert.Equal("premium-plan", user?.PlanId);
        }

        [Fact]
        public async Task GetAllPlans_Unauthenticated_ReturnsUnauthorized()
        {
            // Act
            var client = _factory.CreateClient();
            var response = await client.GetAsync("/api/admin/plans");

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task GetAllPlans_WithAdminRole_ReturnsPlans()
        {
            // Arrange
            var mockUserService = new Mock<ICurrentUserService>();
            mockUserService.Setup(s => s.IsAuthenticated).Returns(true);
            mockUserService.Setup(s => s.IsInRole("Admin")).Returns(true);

            var mockPlanRepository = new Mock<IPlanRepository>();
            mockPlanRepository
                .Setup(r => r.GetAllPlansAsync())
                .ReturnsAsync(new[]
                {
                    new UserPlan 
                    { 
                        Id = "free-plan",
                        Name = "Free",
                        DailyRequestLimit = 100,
                        IsPaid = false
                    },
                    new UserPlan 
                    { 
                        Id = "premium-plan",
                        Name = "Premium",
                        DailyRequestLimit = 500,
                        IsPaid = true
                    }
                });

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton(mockUserService.Object);
                    services.AddSingleton(mockPlanRepository.Object);
                });
            }).CreateClient();

            // Act
            var response = await client.GetAsync("/api/admin/plans");

            // Assert
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            var plans = System.Text.Json.JsonSerializer.Deserialize<UserPlan[]>(content, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            Assert.NotNull(plans);
            Assert.Equal(2, plans?.Length);
        }

        [Fact]
        public async Task CreatePlan_WithAdminRole_Succeeds()
        {
            // Arrange
            var mockUserService = new Mock<ICurrentUserService>();
            mockUserService.Setup(s => s.IsAuthenticated).Returns(true);
            mockUserService.Setup(s => s.IsInRole("Admin")).Returns(true);

            var mockPlanRepository = new Mock<IPlanRepository>();
            mockPlanRepository
                .Setup(r => r.CreatePlanAsync(It.IsAny<UserPlan>()))
                .ReturnsAsync((UserPlan plan) => plan);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton(mockUserService.Object);
                    services.AddSingleton(mockPlanRepository.Object);
                });
            }).CreateClient();

            // Act
            var response = await client.PostAsync("/api/admin/plans", 
                new StringContent(
                    """{"name": "Enterprise", "dailyRequestLimit": 1000, "isPaid": true}""",
                    Encoding.UTF8,
                    "application/json"));

            // Assert
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        [Fact]
        public async Task UpdatePlan_WithAdminRole_Succeeds()
        {
            // Arrange
            var mockUserService = new Mock<ICurrentUserService>();
            mockUserService.Setup(s => s.IsAuthenticated).Returns(true);
            mockUserService.Setup(s => s.IsInRole("Admin")).Returns(true);

            var mockPlanRepository = new Mock<IPlanRepository>();
            var existingPlan = new UserPlan
            {
                Id = "premium-plan",
                Name = "Premium",
                DailyRequestLimit = 500,
                IsPaid = true
            };
            mockPlanRepository
                .Setup(r => r.GetPlanByIdAsync("premium-plan"))
                .ReturnsAsync(existingPlan);
            mockPlanRepository
                .Setup(r => r.UpdatePlanAsync(It.IsAny<UserPlan>()))
                .ReturnsAsync(true);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton(mockUserService.Object);
                    services.AddSingleton(mockPlanRepository.Object);
                });
            }).CreateClient();

            // Act
            var response = await client.PutAsync("/api/admin/plans/premium-plan", 
                new StringContent(
                    """{"name": "Premium Updated", "dailyRequestLimit": 600}""",
                    Encoding.UTF8,
                    "application/json"));

            // Assert
            response.EnsureSuccessStatusCode();
        }

        [Fact]
        public async Task DeletePlan_WithAdminRole_Succeeds()
        {
            // Arrange
            var mockUserService = new Mock<ICurrentUserService>();
            mockUserService.Setup(s => s.IsAuthenticated).Returns(true);
            mockUserService.Setup(s => s.IsInRole("Admin")).Returns(true);

            var mockPlanRepository = new Mock<IPlanRepository>();
            mockPlanRepository
                .Setup(r => r.GetPlanByIdAsync("premium-plan"))
                .ReturnsAsync(new UserPlan 
                { 
                    Id = "premium-plan",
                    Name = "Premium",
                    DailyRequestLimit = 500,
                    IsPaid = true
                });
            mockPlanRepository
                .Setup(r => r.DeletePlanAsync("premium-plan"))
                .ReturnsAsync(true);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton(mockUserService.Object);
                    services.AddSingleton(mockPlanRepository.Object);
                });
            }).CreateClient();

            // Act
            var response = await client.DeleteAsync("/api/admin/plans/premium-plan");

            // Assert
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
    }
}