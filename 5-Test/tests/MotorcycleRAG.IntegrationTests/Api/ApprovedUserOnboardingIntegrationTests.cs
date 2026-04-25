using System.Net;
using System.Text;
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

namespace MotorcycleRAG.IntegrationTests.Api;

public class ApprovedUserOnboardingIntegrationTests : IClassFixture<TestWebApplicationFactory> {
    private readonly TestWebApplicationFactory _factory;

    public ApprovedUserOnboardingIntegrationTests(TestWebApplicationFactory factory) {
        _factory = factory;
    }

    [Fact]
    public async Task ApproveRequest_SeedsIdentityAndReturnsCompletedManagementRow() {
        var context = new OnboardingContext();
        var pending = CreateRequest("request-approval");
        var completedRow = new UserManagementRow {
            RowId = "request:request-approval",
            RowType = "request",
            AccessRequestId = "request-approval",
            ManagedUserId = "managed-1",
            Email = pending.Email,
            Provider = pending.Provider,
            AssignedTier = TierLabel.Trial,
            RequestDecisionState = RequestDecisionState.Approved,
            OnboardingExecutionState = OnboardingExecutionState.Completed,
            ManagedUserAccessState = ManagedUserAccessState.Active,
            RowState = UserManagementRowState.Active,
            RowVersion = "rv-2"
        };

        context.AccessRequestRepository.Setup(repository => repository.GetAdminRecordByRequestIdAsync(pending.RequestId))
            .ReturnsAsync(pending);
        context.AccessRequestRepository.Setup(repository => repository.BeginApprovalOnboardingAsync(pending.RequestId, TierLabel.Trial, "rv-1", It.IsAny<string?>()))
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
        context.PlanRepository.Setup(repository => repository.GetPlanByNameAsync("Free"))
            .ReturnsAsync(new UserPlan { Id = "plan-free", Name = "Free" });
        context.UserRepository.Setup(repository => repository.GetUserByEmailAsync(pending.Email))
            .ReturnsAsync((UserDTO?)null);
        context.UserRepository.Setup(repository => repository.CreateUserAsync(It.IsAny<UserDTO>()))
            .ReturnsAsync(new UserDTO {
                Id = "managed-1",
                Email = pending.Email,
                DisplayName = pending.Email,
                IsEnabled = true,
                AccessState = ManagedUserAccessState.Active,
                PlanId = "plan-free",
                TierLabel = TierLabel.Trial,
                AuthProvider = pending.Provider.ToString()
            });
        context.UsageTrackingService.Setup(service => service.SeedOnboardingAccessAsync("managed-1", pending.RequestId))
            .ReturnsAsync(new Usage { Id = 1, UserId = "managed-1" });
        context.ExternalIdentityProvisioningService
            .Setup(service => service.ProvisionApprovedUserAsync(pending.Email, pending.Email, TierLabel.Trial, pending.Provider))
            .ReturnsAsync("external-1");
        context.UserIdentityRepository
            .Setup(repository => repository.UpsertAsync("managed-1", pending.Provider, pending.Email, null, null, null, "external-1"))
            .ReturnsAsync(true);
        context.AccessRequestRepository.Setup(repository => repository.CompleteOnboardingAsync(pending.RequestId, "managed-1", "external-1"))
            .ReturnsAsync(pending);
        context.UserManagementQueryRepository.Setup(repository => repository.GetRowByIdAsync("request:request-approval"))
            .ReturnsAsync(completedRow);

        using var factory = CreateFactory(context);
        using var client = factory.CreateClientWithRoles("mcr-api-admin");
        using var content = new StringContent("""{"tier":"trial","expectedRowVersion":"rv-1"}""", Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/admin/access-requests/request-approval/approve", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        context.UserRepository.Verify(repository => repository.CreateUserAsync(It.Is<UserDTO>(user =>
            user.Email == pending.Email &&
            user.AccessState == ManagedUserAccessState.Active &&
            user.TierLabel == TierLabel.Trial)), Times.Once);
        context.UsageTrackingService.Verify(service => service.SeedOnboardingAccessAsync("managed-1", pending.RequestId), Times.Once);
        context.UserIdentityRepository.Verify(repository => repository.UpsertAsync("managed-1", pending.Provider, pending.Email, null, null, null, "external-1"), Times.Once);
    }

    [Fact]
    public async Task ExistingUserCancellation_RevokesExternalAccess() {
        var context = new OnboardingContext();
        context.UserManagementQueryRepository
            .SetupSequence(repository => repository.GetRowByIdAsync("user:managed-1"))
            .ReturnsAsync(new UserManagementRow {
                RowId = "user:managed-1",
                ManagedUserId = "managed-1",
                RowVersion = "rv-1",
                ManagedUserAccessState = ManagedUserAccessState.Active,
                RowState = UserManagementRowState.Active
            })
            .ReturnsAsync(new UserManagementRow {
                RowId = "user:managed-1",
                ManagedUserId = "managed-1",
                RowVersion = "rv-2",
                ManagedUserAccessState = ManagedUserAccessState.Cancelled,
                RowState = UserManagementRowState.Cancelled
            });
        context.UserRepository.Setup(repository => repository.GetUserByIdAsync("managed-1"))
            .ReturnsAsync(new UserDTO { Id = "managed-1", Email = "rider@example.com", AccessState = ManagedUserAccessState.Active, IsEnabled = true });
        context.UserRepository.Setup(repository => repository.UpdateAccessStateAsync("managed-1", ManagedUserAccessState.Cancelled, false, It.IsAny<string?>(), "cancelled"))
            .ReturnsAsync(true);
        context.UserIdentityRepository.Setup(repository => repository.GetActiveByManagedUserIdAsync("managed-1"))
            .ReturnsAsync(new UserIdentityLinkRecord {
                ManagedUserId = "managed-1",
                Provider = IdentityProvider.Microsoft,
                ProviderEmail = "rider@example.com",
                ExternalDirectoryObjectId = "external-1"
            });

        using var factory = CreateFactory(context);
        using var client = factory.CreateClientWithRoles("mcr-api-admin");
        using var content = new StringContent("""{"expectedRowVersion":"rv-1","reason":"cancelled"}""", Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/admin/users/managed-1/cancel", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        context.ExternalIdentityProvisioningService.Verify(service => service.RevokeAccessAsync("external-1"), Times.Once);
        context.UserIdentityRepository.Verify(repository => repository.MarkAccessRevokedAsync("managed-1"), Times.Once);
    }

    private WebApplicationFactory<Program> CreateFactory(OnboardingContext context) {
        return _factory.WithWebHostBuilder(builder => {
            builder.ConfigureServices(services => {
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

    private static AccessRequestAdminRecord CreateRequest(string requestId) {
        return new AccessRequestAdminRecord {
            RequestId = requestId,
            Email = "rider@example.com",
            Provider = IdentityProvider.Microsoft,
            RequestDecisionState = RequestDecisionState.Pending,
            OnboardingExecutionState = OnboardingExecutionState.NotStarted,
            AssignedTier = TierLabel.Trial,
            CorrelationId = "corr-001",
            RequestedAtUtc = DateTime.UtcNow,
            RowVersion = "rv-1"
        };
    }

    private sealed class OnboardingContext {
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
