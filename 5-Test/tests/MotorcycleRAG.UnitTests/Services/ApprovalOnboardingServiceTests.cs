using Microsoft.Extensions.Logging;
using Moq;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.UnitTests.Services;

public class ApprovalOnboardingServiceTests {
    private readonly Mock<IAccessRequestRepository> _accessRequestRepository = new();
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IUserIdentityRepository> _userIdentityRepository = new();
    private readonly Mock<IPlanRepository> _planRepository = new();
    private readonly Mock<IUsageTrackingService> _usageTrackingService = new();
    private readonly Mock<IExternalIdentityProvisioningService> _externalIdentityProvisioningService = new();
    private readonly ApprovalOnboardingService _service;

    public ApprovalOnboardingServiceTests() {
        var tierMappingLogger = new Mock<ILogger<TierEntitlementMappingService>>();
        var serviceLogger = new Mock<ILogger<ApprovalOnboardingService>>();

        _service = new ApprovalOnboardingService(
            _accessRequestRepository.Object,
            _userRepository.Object,
            _userIdentityRepository.Object,
            _planRepository.Object,
            _usageTrackingService.Object,
            _externalIdentityProvisioningService.Object,
            new TierEntitlementMappingService(tierMappingLogger.Object),
            serviceLogger.Object);
    }

    [Fact]
    public async Task ExecuteAsync_CreatesUserSeedsUsageAndCompletesRequest() {
        var request = CreateRequest();
        var createdUser = new UserDTO {
            Id = "managed-1",
            Email = request.Email,
            DisplayName = request.Email,
            PlanId = "plan-free",
            TierLabel = TierLabel.Trial,
            AccessState = ManagedUserAccessState.Active,
            IsEnabled = true,
            AuthProvider = request.Provider.ToString()
        };

        _planRepository.Setup(r => r.GetPlanByNameAsync("Free"))
            .ReturnsAsync(new UserPlan { Id = "plan-free", Name = "Free" });
        _userRepository.Setup(r => r.GetUserByEmailAsync(request.Email))
            .ReturnsAsync((UserDTO?)null);
        _userRepository.Setup(r => r.CreateUserAsync(It.IsAny<UserDTO>()))
            .ReturnsAsync(createdUser);
        _usageTrackingService.Setup(s => s.SeedOnboardingAccessAsync("managed-1", request.RequestId))
            .ReturnsAsync(new Usage { Id = 1, UserId = "managed-1", QueryId = $"onboarding-seed:{request.RequestId}" });
        _externalIdentityProvisioningService
            .Setup(s => s.ProvisionApprovedUserAsync(request.Email, request.Email, TierLabel.Trial, request.Provider))
            .ReturnsAsync("external-123");
        _userIdentityRepository
            .Setup(r => r.UpsertAsync("managed-1", request.Provider, request.Email, null, null, null, "external-123"))
            .ReturnsAsync(true);
        _accessRequestRepository
            .Setup(r => r.CompleteOnboardingAsync(request.RequestId, "managed-1", "external-123"))
            .ReturnsAsync(new AccessRequestAdminRecord { RequestId = request.RequestId, ManagedUserId = "managed-1", ExternalDirectoryObjectId = "external-123" });

        await _service.ExecuteAsync(request);

        _userRepository.Verify(r => r.CreateUserAsync(It.Is<UserDTO>(u =>
            u.Email == request.Email
            && u.PlanId == "plan-free"
            && u.TierLabel == TierLabel.Trial
            && u.AccessState == ManagedUserAccessState.Active)), Times.Once);
        _usageTrackingService.Verify(s => s.SeedOnboardingAccessAsync("managed-1", request.RequestId), Times.Once);
        _userIdentityRepository.Verify(r => r.UpsertAsync("managed-1", request.Provider, request.Email, null, null, null, "external-123"), Times.Once);
        _accessRequestRepository.Verify(r => r.CompleteOnboardingAsync(request.RequestId, "managed-1", "external-123"), Times.Once);
        _accessRequestRepository.Verify(r => r.FailOnboardingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenExternalProvisioningFails_MarksRequestFailed() {
        var request = CreateRequest();
        var existingUser = new UserDTO {
            Id = "managed-1",
            Email = request.Email,
            DisplayName = request.Email,
            PlanId = "plan-free",
            TierLabel = TierLabel.Trial,
            AccessState = ManagedUserAccessState.Active,
            IsEnabled = true,
            AuthProvider = request.Provider.ToString()
        };

        _planRepository.Setup(r => r.GetPlanByNameAsync("Free"))
            .ReturnsAsync(new UserPlan { Id = "plan-free", Name = "Free" });
        _userRepository.Setup(r => r.GetUserByEmailAsync(request.Email))
            .ReturnsAsync(existingUser);
        _userRepository.Setup(r => r.UpdateUserAsync(It.IsAny<UserDTO>()))
            .ReturnsAsync(true);
        _usageTrackingService.Setup(s => s.SeedOnboardingAccessAsync("managed-1", request.RequestId))
            .ReturnsAsync(new Usage { Id = 1, UserId = "managed-1", QueryId = $"onboarding-seed:{request.RequestId}" });
        _externalIdentityProvisioningService
            .Setup(s => s.ProvisionApprovedUserAsync(request.Email, request.Email, TierLabel.Trial, request.Provider))
            .ThrowsAsync(new InvalidOperationException("Graph unavailable"));
        _accessRequestRepository
            .Setup(r => r.FailOnboardingAsync(request.RequestId, "ProvisionExternalIdentity", It.IsAny<string>(), "managed-1", null))
            .ReturnsAsync(new AccessRequestAdminRecord { RequestId = request.RequestId, OnboardingExecutionState = OnboardingExecutionState.Failed });

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ExecuteAsync(request));

        _accessRequestRepository.Verify(r => r.FailOnboardingAsync(
            request.RequestId,
            "ProvisionExternalIdentity",
            It.Is<string>(message => message.Contains("Graph unavailable", StringComparison.Ordinal)),
            "managed-1",
            null), Times.Once);
        _accessRequestRepository.Verify(r => r.CompleteOnboardingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    private static AccessRequestAdminRecord CreateRequest() {
        return new AccessRequestAdminRecord {
            RequestId = Guid.NewGuid().ToString(),
            Email = "rider@example.com",
            Provider = IdentityProvider.Google,
            RequestDecisionState = RequestDecisionState.Approved,
            OnboardingExecutionState = OnboardingExecutionState.InProgress,
            AssignedTier = TierLabel.Trial,
            CorrelationId = "corr-001",
            RequestedAtUtc = DateTime.UtcNow,
            RowVersion = "0x0000000000000001"
        };
    }
}