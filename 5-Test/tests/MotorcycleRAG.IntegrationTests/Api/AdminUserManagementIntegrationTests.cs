using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MotorcycleRAG.API;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using Xunit;
using MotorcycleRAG.IntegrationTests;


namespace MotorcycleRAG.IntegrationTests.Api {
    /// <summary>
    /// Integration tests for admin user management and plan assignment
    /// </summary>
    public class AdminUserManagementIntegrationTests : IClassFixture<TestWebApplicationFactory> {
        private readonly TestWebApplicationFactory _factory;
        private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        public AdminUserManagementIntegrationTests(TestWebApplicationFactory factory) {
            _factory = factory;
        }

        [Fact]
        public async Task SetUserEnabledStatus_Unauthenticated_ReturnsUnauthorized() {
            // Act
            var client = _factory.CreateClient();
            using var content = new StringContent(
                """{"isEnabled": true}""",
                Encoding.UTF8,
                "application/json");
            var response = await client.PutAsync("/api/admin/users/user-1/enabled", content);

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task SetUserEnabledStatus_WithoutAdminRole_ReturnsForbidden() {
            // Arrange
            var mockUserService = new Mock<ICurrentUserService>();
            mockUserService.Setup(s => s.UserId).Returns("test-user-1");
            mockUserService.Setup(s => s.IsAuthenticated).Returns(true);
            mockUserService.Setup(s => s.IsInRole("mcr-api-admin")).Returns(false);

            using var factory = _factory.WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.AddSingleton(mockUserService.Object);
                });
            });
            using var client = factory.CreateClientWithRoles("User");

            // Act
            using var content = new StringContent(
                """{"isEnabled": true}""",
                Encoding.UTF8,
                "application/json");
            var response = await client.PutAsync("/api/admin/users/user-1/enabled", content);

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task SetUserEnabledStatus_WithAdminRole_Succeeds() {
            // Arrange
            var mockUserService = new Mock<ICurrentUserService>();
            mockUserService.Setup(s => s.UserId).Returns("test-user-1");
            mockUserService.Setup(s => s.IsAuthenticated).Returns(true);
            mockUserService.Setup(s => s.IsInRole("mcr-api-admin")).Returns(true);

            var mockUserAdminService = new Mock<IUserAdminService>();
            mockUserAdminService
                .Setup(s => s.SetUserEnabledStatusAsync("test-user-1", true))
                .ReturnsAsync(new UserDTO {
                    Id = "test-user-1",
                    Email = "test@example.com",
                    IsEnabled = true
                });

            using var factory = _factory.WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.AddSingleton(mockUserService.Object);
                    services.AddSingleton(mockUserAdminService.Object);
                });
            });
            using var client = factory.CreateClientWithRoles("mcr-api-admin");

            // Act
            using var content = new StringContent(
                """{"isEnabled": true}""",
                Encoding.UTF8,
                "application/json");
            var response = await client.PutAsync("/api/admin/users/test-user-1/enabled", content);

            // Assert
            response.EnsureSuccessStatusCode();
            var responseContent = await response.Content.ReadAsStringAsync();
            var user = System.Text.Json.JsonSerializer.Deserialize<UserDTO>(responseContent, JsonOptions);

            Assert.NotNull(user);
            Assert.True(user?.IsEnabled);
        }

        [Fact]
        public async Task AssignPlanToUser_WithAdminRole_Succeeds() {
            // Arrange
            var mockUserService = new Mock<ICurrentUserService>();
            mockUserService.Setup(s => s.UserId).Returns("test-user-1");
            mockUserService.Setup(s => s.IsAuthenticated).Returns(true);
            mockUserService.Setup(s => s.IsInRole("mcr-api-admin")).Returns(true);

            var mockUserAdminService = new Mock<IUserAdminService>();
            mockUserAdminService
                .Setup(s => s.AssignPlanToUserAsync("test-user-1", "premium-plan"))
                .ReturnsAsync(new UserDTO {
                    Id = "test-user-1",
                    Email = "test@example.com",
                    PlanId = "premium-plan"
                });

            using var factory = _factory.WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.AddSingleton(mockUserService.Object);
                    services.AddSingleton(mockUserAdminService.Object);
                });
            });
            using var client = factory.CreateClientWithRoles("mcr-api-admin");

            // Act
            using var content = new StringContent(
                """{"planId": "premium-plan"}""",
                Encoding.UTF8,
                "application/json");
            var response = await client.PutAsync("/api/admin/users/test-user-1/plan", content);

            // Assert
            response.EnsureSuccessStatusCode();
            var responseContent = await response.Content.ReadAsStringAsync();
            var user = System.Text.Json.JsonSerializer.Deserialize<UserDTO>(responseContent, JsonOptions);

            Assert.NotNull(user);
            Assert.Equal("premium-plan", user?.PlanId);
        }

        [Fact]
        public async Task GetAllPlans_Unauthenticated_ReturnsUnauthorized() {
            // Act
            var client = _factory.CreateClient();
            var response = await client.GetAsync("/api/admin/plans");

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task GetAllPlans_WithAdminRole_ReturnsPlans() {
            // Arrange
            var mockUserService = new Mock<ICurrentUserService>();
            mockUserService.Setup(s => s.IsAuthenticated).Returns(true);
            mockUserService.Setup(s => s.IsInRole("mcr-api-admin")).Returns(true);

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

            using var factory = _factory.WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.AddSingleton(mockUserService.Object);
                    services.AddSingleton(mockPlanRepository.Object);
                });
            });
            using var client = factory.CreateClientWithRoles("mcr-api-admin");

            // Act
            var response = await client.GetAsync("/api/admin/plans");

            // Assert
            response.EnsureSuccessStatusCode();
            var responseContent = await response.Content.ReadAsStringAsync();
            var plans = System.Text.Json.JsonSerializer.Deserialize<UserPlan[]>(responseContent, JsonOptions);

            Assert.NotNull(plans);
            Assert.Equal(2, plans?.Length);
        }

        [Fact]
        public async Task CreatePlan_WithAdminRole_Succeeds() {
            // Arrange
            var mockUserService = new Mock<ICurrentUserService>();
            mockUserService.Setup(s => s.IsAuthenticated).Returns(true);
            mockUserService.Setup(s => s.IsInRole("mcr-api-admin")).Returns(true);

            var mockPlanRepository = new Mock<IPlanRepository>();
            mockPlanRepository
                .Setup(r => r.CreatePlanAsync(It.IsAny<UserPlan>()))
                .ReturnsAsync((UserPlan plan) => plan);

            using var factory = _factory.WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.AddSingleton(mockUserService.Object);
                    services.AddSingleton(mockPlanRepository.Object);
                });
            });
            using var client = factory.CreateClientWithRoles("mcr-api-admin");

            // Act
            using var content = new StringContent(
                """{"name": "Enterprise", "dailyRequestLimit": 1000, "isPaid": true}""",
                Encoding.UTF8,
                "application/json");
            var response = await client.PostAsync("/api/admin/plans", content);

            // Assert
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        [Fact]
        public async Task UpdatePlan_WithAdminRole_Succeeds() {
            // Arrange
            var mockUserService = new Mock<ICurrentUserService>();
            mockUserService.Setup(s => s.IsAuthenticated).Returns(true);
            mockUserService.Setup(s => s.IsInRole("mcr-api-admin")).Returns(true);

            var mockPlanRepository = new Mock<IPlanRepository>();
            var existingPlan = new UserPlan {
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

            using var factory = _factory.WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.AddSingleton(mockUserService.Object);
                    services.AddSingleton(mockPlanRepository.Object);
                });
            });
            using var client = factory.CreateClientWithRoles("mcr-api-admin");

            // Act
            using var content = new StringContent(
                """{"name": "Premium Updated", "dailyRequestLimit": 600}""",
                Encoding.UTF8,
                "application/json");
            var response = await client.PutAsync("/api/admin/plans/premium-plan", content);

            // Assert
            response.EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task DeletePlan_WithAdminRole_Succeeds() {
            // Arrange
            var mockUserService = new Mock<ICurrentUserService>();
            mockUserService.Setup(s => s.IsAuthenticated).Returns(true);
            mockUserService.Setup(s => s.IsInRole("mcr-api-admin")).Returns(true);

            var mockPlanRepository = new Mock<IPlanRepository>();
            mockPlanRepository
                .Setup(r => r.GetPlanByIdAsync("premium-plan"))
                .ReturnsAsync(new UserPlan {
                    Id = "premium-plan",
                    Name = "Premium",
                    DailyRequestLimit = 500,
                    IsPaid = true
                });
            mockPlanRepository
                .Setup(r => r.DeletePlanAsync("premium-plan"))
                .ReturnsAsync(true);

            using var factory = _factory.WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.AddSingleton(mockUserService.Object);
                    services.AddSingleton(mockPlanRepository.Object);
                });
            });
            using var client = factory.CreateClientWithRoles("mcr-api-admin");

            // Act
            var response = await client.DeleteAsync("/api/admin/plans/premium-plan");

            // Assert
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        [Fact]
        public async Task UserManagement_Unauthenticated_ReturnsUnauthorized() {
            using var client = _factory.CreateClient();

            var response = await client.GetAsync("/api/admin/user-management");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task UserManagement_WithAdminRole_ReturnsUnifiedRows() {
            var row = CreatePendingRow("request-1");
            using var factory = CreateFactoryForAdminSurface(row);
            using var client = factory.CreateClientWithRoles("mcr-api-admin");

            var response = await client.GetAsync("/api/admin/user-management?page=1&pageSize=25");

            response.EnsureSuccessStatusCode();
            var responseContent = await response.Content.ReadAsStringAsync();
            var list = System.Text.Json.JsonSerializer.Deserialize<UserManagementListResponse>(responseContent, JsonOptions);

            Assert.NotNull(list);
            Assert.Single(list!.Rows);
            Assert.Equal("request:request-1", list.Rows[0].RowId);
        }

        [Fact]
        public async Task ApproveAccessRequest_WithAdminRole_CompletesOnboarding() {
            var row = CreatePendingRow("request-2");
            row.RowState = UserManagementRowState.Active;
            row.RequestDecisionState = RequestDecisionState.Approved;
            row.OnboardingExecutionState = OnboardingExecutionState.Completed;
            row.ManagedUserId = "managed-1";
            row.AssignedTier = TierLabel.Trial;

            using var factory = CreateFactoryForAdminSurface(row, configure: context => {
                var pending = CreateAdminRecord("request-2");
                context.AccessRequestRepository
                    .Setup(repository => repository.GetAdminRecordByRequestIdAsync("request-2"))
                    .ReturnsAsync(pending);
                context.AccessRequestRepository
                    .Setup(repository => repository.BeginApprovalOnboardingAsync("request-2", TierLabel.Trial, "rv-1", It.IsAny<string?>()))
                    .ReturnsAsync(new AccessRequestAdminRecord {
                        RequestId = pending.RequestId,
                        Email = pending.Email,
                        Provider = pending.Provider,
                        RequestDecisionState = RequestDecisionState.Approved,
                        OnboardingExecutionState = OnboardingExecutionState.InProgress,
                        AssignedTier = TierLabel.Trial,
                        CorrelationId = pending.CorrelationId,
                        RowVersion = pending.RowVersion,
                        RequestedAtUtc = pending.RequestedAtUtc
                    });
                ConfigureSuccessfulOnboarding(context, pending);
            });
            using var client = factory.CreateClientWithRoles("mcr-api-admin");

            using var content = new StringContent(
                """{"tier":"trial","expectedRowVersion":"rv-1"}""",
                Encoding.UTF8,
                "application/json");
            var response = await client.PostAsync("/api/admin/access-requests/request-2/approve", content);

            response.EnsureSuccessStatusCode();
            var responseContent = await response.Content.ReadAsStringAsync();
            var actionResponse = System.Text.Json.JsonSerializer.Deserialize<AdminActionResponse>(responseContent, JsonOptions);

            Assert.NotNull(actionResponse);
            Assert.Equal(UserManagementRowState.Active, actionResponse!.Row.RowState);
        }

        [Fact]
        public async Task ChangeTierAndCancelManagedUser_WithAdminRole_ReturnsUpdatedRows() {
            var activeRow = new UserManagementRow {
                RowId = "user:managed-1",
                RowType = "user",
                ManagedUserId = "managed-1",
                Email = "rider@example.com",
                Provider = IdentityProvider.Microsoft,
                AssignedTier = TierLabel.RoadRunner,
                ManagedUserAccessState = ManagedUserAccessState.Active,
                RowState = UserManagementRowState.Active,
                RowVersion = "rv-1"
            };

            using var factory = CreateFactoryForAdminSurface(activeRow, configure: context => {
                context.UserManagementQueryRepository
                    .SetupSequence(repository => repository.GetRowByIdAsync("user:managed-1"))
                    .ReturnsAsync(activeRow)
                    .ReturnsAsync(CreateUserRow("managed-1", TierLabel.Admin, ManagedUserAccessState.Active, UserManagementRowState.Active, "rv-2"))
                    .ReturnsAsync(CreateUserRow("managed-1", TierLabel.Admin, ManagedUserAccessState.Active, UserManagementRowState.Active, "rv-2"))
                    .ReturnsAsync(CreateUserRow("managed-1", TierLabel.Admin, ManagedUserAccessState.Cancelled, UserManagementRowState.Cancelled, "rv-3"));
                context.UserRepository.Setup(repository => repository.GetUserByIdAsync("managed-1"))
                    .ReturnsAsync(new UserDTO { Id = "managed-1", Email = "rider@example.com", AccessState = ManagedUserAccessState.Active });
                context.PlanRepository.Setup(repository => repository.GetPlanByNameAsync("Pro"))
                    .ReturnsAsync(new UserPlan { Id = "plan-pro", Name = "Pro" });
                context.UserRepository.Setup(repository => repository.AssignTierAsync("managed-1", "plan-pro", TierLabel.Admin))
                    .ReturnsAsync(true);
                context.UserRepository.Setup(repository => repository.UpdateAccessStateAsync("managed-1", ManagedUserAccessState.Cancelled, false, It.IsAny<string?>(), "done"))
                    .ReturnsAsync(true);
                context.UserIdentityRepository.Setup(repository => repository.GetActiveByManagedUserIdAsync("managed-1"))
                    .ReturnsAsync(new UserIdentityLinkRecord {
                        ManagedUserId = "managed-1",
                        Provider = IdentityProvider.Microsoft,
                        ProviderEmail = "rider@example.com",
                        ExternalDirectoryObjectId = "external-1"
                    });
            });
            using var client = factory.CreateClientWithRoles("mcr-api-admin");

            using var tierContent = new StringContent("""{"tier":"admin","expectedRowVersion":"rv-1"}""", Encoding.UTF8, "application/json");
            var tierResponse = await client.PostAsync("/api/admin/users/managed-1/change-tier", tierContent);
            tierResponse.EnsureSuccessStatusCode();

            using var cancelContent = new StringContent("""{"expectedRowVersion":"rv-2","reason":"done"}""", Encoding.UTF8, "application/json");
            var cancelResponse = await client.PostAsync("/api/admin/users/managed-1/cancel", cancelContent);
            cancelResponse.EnsureSuccessStatusCode();

            Assert.Equal(HttpStatusCode.OK, tierResponse.StatusCode);
            Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        }

        private WebApplicationFactory<Program> CreateFactoryForAdminSurface(
            UserManagementRow row,
            Action<AdminSurfaceContext>? configure = null) {
            return _factory.WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    var context = new AdminSurfaceContext();
                    context.UserManagementQueryRepository
                        .Setup(repository => repository.GetRowsAsync(It.IsAny<UserManagementRowState?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>()))
                        .ReturnsAsync(new UserManagementListResponse { Rows = [row], Page = 1, PageSize = 50, TotalCount = 1 });
                    context.UserManagementQueryRepository
                        .Setup(repository => repository.GetRowByIdAsync(row.RowId))
                        .ReturnsAsync(row);

                    configure?.Invoke(context);

                    var tierMapping = new TierEntitlementMappingService(NullLogger<TierEntitlementMappingService>.Instance);
                    var approvalService = new ApprovalOnboardingService(
                        context.AccessRequestRepository.Object,
                        context.UserRepository.Object,
                        context.UserIdentityRepository.Object,
                        context.PlanRepository.Object,
                        context.UsageTrackingService.Object,
                        context.ExternalIdentityProvisioningService.Object,
                        tierMapping,
                        context.TelemetryService.Object,
                        NullLogger<ApprovalOnboardingService>.Instance);
                    var lifecycleService = new UserAccessLifecycleService(
                        context.UserRepository.Object,
                        context.UserIdentityRepository.Object,
                        context.PlanRepository.Object,
                        context.UserManagementQueryRepository.Object,
                        context.ExternalIdentityProvisioningService.Object,
                        tierMapping,
                        context.TelemetryService.Object,
                        NullLogger<UserAccessLifecycleService>.Instance);
                    var adminService = new AccessRequestAdminService(
                        context.AccessRequestRepository.Object,
                        context.UserManagementQueryRepository.Object,
                        approvalService,
                        lifecycleService,
                        context.TelemetryService.Object,
                        NullLogger<AccessRequestAdminService>.Instance);

                    services.AddSingleton(lifecycleService);
                    services.AddSingleton(adminService);
                });
            });
        }

        private static UserManagementRow CreatePendingRow(string requestId) {
            return new UserManagementRow {
                RowId = $"request:{requestId}",
                RowType = "request",
                AccessRequestId = requestId,
                Email = "rider@example.com",
                Provider = IdentityProvider.Microsoft,
                RequestDecisionState = RequestDecisionState.Pending,
                OnboardingExecutionState = OnboardingExecutionState.NotStarted,
                RowState = UserManagementRowState.PendingApproval,
                RowVersion = "rv-1",
                CorrelationId = "corr-001"
            };
        }

        private static AccessRequestAdminRecord CreateAdminRecord(string requestId) {
            return new AccessRequestAdminRecord {
                RequestId = requestId,
                Email = "rider@example.com",
                Provider = IdentityProvider.Microsoft,
                RequestDecisionState = RequestDecisionState.Pending,
                OnboardingExecutionState = OnboardingExecutionState.NotStarted,
                AssignedTier = TierLabel.Trial,
                CorrelationId = "corr-001",
                RowVersion = "rv-1",
                RequestedAtUtc = DateTime.UtcNow
            };
        }

        private static UserManagementRow CreateUserRow(
            string managedUserId,
            TierLabel tier,
            ManagedUserAccessState accessState,
            UserManagementRowState rowState,
            string rowVersion) {
            return new UserManagementRow {
                RowId = $"user:{managedUserId}",
                RowType = "user",
                ManagedUserId = managedUserId,
                Email = "rider@example.com",
                Provider = IdentityProvider.Microsoft,
                AssignedTier = tier,
                ManagedUserAccessState = accessState,
                RowState = rowState,
                RowVersion = rowVersion
            };
        }

        private static void ConfigureSuccessfulOnboarding(AdminSurfaceContext context, AccessRequestAdminRecord request) {
            context.PlanRepository.Setup(repository => repository.GetPlanByNameAsync("Free"))
                .ReturnsAsync(new UserPlan { Id = "plan-free", Name = "Free" });
            context.UserRepository.Setup(repository => repository.GetUserByEmailAsync(request.Email))
                .ReturnsAsync((UserDTO?)null);
            context.UserRepository.Setup(repository => repository.CreateUserAsync(It.IsAny<UserDTO>()))
                .ReturnsAsync(new UserDTO {
                    Id = "managed-1",
                    Email = request.Email,
                    DisplayName = request.Email,
                    AccessState = ManagedUserAccessState.Active,
                    IsEnabled = true,
                    PlanId = "plan-free",
                    TierLabel = TierLabel.Trial,
                    AuthProvider = request.Provider.ToString()
                });
            context.UsageTrackingService.Setup(service => service.SeedOnboardingAccessAsync("managed-1", request.RequestId))
                .ReturnsAsync(new Usage { Id = 1, UserId = "managed-1" });
            context.ExternalIdentityProvisioningService
                .Setup(service => service.ProvisionApprovedUserAsync(request.Email, request.Email, TierLabel.Trial, request.Provider))
                .ReturnsAsync("external-1");
            context.UserIdentityRepository
                .Setup(repository => repository.UpsertAsync("managed-1", request.Provider, request.Email, null, null, null, "external-1"))
                .ReturnsAsync(true);
            context.AccessRequestRepository
                .Setup(repository => repository.CompleteOnboardingAsync(request.RequestId, "managed-1", "external-1"))
                .ReturnsAsync(request);
        }

        private sealed class AdminSurfaceContext {
            public Mock<IAccessRequestRepository> AccessRequestRepository { get; } = new();
            public Mock<IUserManagementQueryRepository> UserManagementQueryRepository { get; } = new();
            public Mock<IUserRepository> UserRepository { get; } = new();
            public Mock<IUserIdentityRepository> UserIdentityRepository { get; } = new();
            public Mock<IPlanRepository> PlanRepository { get; } = new();
            public Mock<IUsageTrackingService> UsageTrackingService { get; } = new();
            public Mock<IExternalIdentityProvisioningService> ExternalIdentityProvisioningService { get; } = new();
            public Mock<ITelemetryService> TelemetryService { get; } = new();
        }
    }
}
