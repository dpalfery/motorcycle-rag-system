using Microsoft.Extensions.Logging;
using Moq;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.UnitTests.Services;

public class UserAccessLifecycleServiceTests {
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IUserIdentityRepository> _userIdentityRepository = new();
    private readonly Mock<IPlanRepository> _planRepository = new();
    private readonly Mock<IUserManagementQueryRepository> _userManagementQueryRepository = new();
    private readonly Mock<IExternalIdentityProvisioningService> _externalIdentityProvisioningService = new();
    private readonly Mock<ITelemetryService> _telemetryService = new();
    private readonly UserAccessLifecycleService _service;

    public UserAccessLifecycleServiceTests() {
        _service = new UserAccessLifecycleService(
            _userRepository.Object,
            _userIdentityRepository.Object,
            _planRepository.Object,
            _userManagementQueryRepository.Object,
            _externalIdentityProvisioningService.Object,
            new TierEntitlementMappingService(new Mock<ILogger<TierEntitlementMappingService>>().Object),
            _telemetryService.Object,
            new Mock<ILogger<UserAccessLifecycleService>>().Object);
    }

    [Fact]
    public async Task ChangeManagedUserTierAsync_WhenRowVersionChanged_ThrowsConcurrencyError() {
        _userManagementQueryRepository
            .Setup(repository => repository.GetRowByIdAsync("user:managed-1"))
            .ReturnsAsync(new UserManagementRow { RowId = "user:managed-1", RowVersion = "current" });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ChangeManagedUserTierAsync(
                "managed-1",
                new ChangeManagedUserTierRequest { Tier = TierLabel.Admin, ExpectedRowVersion = "stale" }));

        Assert.Contains("changed since it was loaded", exception.Message, StringComparison.Ordinal);
        _userRepository.Verify(repository => repository.AssignTierAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TierLabel>()), Times.Never);
    }

    [Fact]
    public async Task ChangeManagedUserTierAsync_ReconcilesExternalRoleAssignment() {
        _userManagementQueryRepository
            .SetupSequence(repository => repository.GetRowByIdAsync("user:managed-1"))
            .ReturnsAsync(new UserManagementRow { RowId = "user:managed-1", RowVersion = "rv-1" })
            .ReturnsAsync(new UserManagementRow { RowId = "user:managed-1", RowVersion = "rv-2", AssignedTier = TierLabel.Admin });
        _userRepository.Setup(repository => repository.GetUserByIdAsync("managed-1"))
            .ReturnsAsync(new UserDTO { Id = "managed-1", Email = "rider@example.com", AccessState = ManagedUserAccessState.Active });
        _planRepository.Setup(repository => repository.GetPlanByNameAsync("Pro"))
            .ReturnsAsync(new UserPlan { Id = "plan-pro", Name = "Pro" });
        _userRepository.Setup(repository => repository.AssignTierAsync("managed-1", "plan-pro", TierLabel.Admin))
            .ReturnsAsync(true);
        _userIdentityRepository.Setup(repository => repository.GetActiveByManagedUserIdAsync("managed-1"))
            .ReturnsAsync(new UserIdentityLinkRecord {
                ManagedUserId = "managed-1",
                Provider = IdentityProvider.Microsoft,
                ProviderEmail = "rider@example.com",
                ExternalDirectoryObjectId = "external-1"
            });

        var response = await _service.ChangeManagedUserTierAsync(
            "managed-1",
            new ChangeManagedUserTierRequest { Tier = TierLabel.Admin, ExpectedRowVersion = "rv-1" });

        Assert.Equal(TierLabel.Admin, response.Row.AssignedTier);
        _externalIdentityProvisioningService.Verify(
            service => service.ReconcileTierAssignmentsAsync("external-1", TierLabel.Admin),
            Times.Once);
    }

    [Fact]
    public async Task CancelManagedUserAsync_DisablesUserAndRevokesIdentityAccess() {
        _userManagementQueryRepository
            .SetupSequence(repository => repository.GetRowByIdAsync("user:managed-1"))
            .ReturnsAsync(new UserManagementRow { RowId = "user:managed-1", RowVersion = "rv-1" })
            .ReturnsAsync(new UserManagementRow {
                RowId = "user:managed-1",
                RowVersion = "rv-2",
                ManagedUserAccessState = ManagedUserAccessState.Cancelled
            });
        _userRepository.Setup(repository => repository.GetUserByIdAsync("managed-1"))
            .ReturnsAsync(new UserDTO { Id = "managed-1", Email = "rider@example.com", AccessState = ManagedUserAccessState.Active });
        _userRepository.Setup(repository => repository.UpdateAccessStateAsync(
                "managed-1",
                ManagedUserAccessState.Cancelled,
                false,
                "admin-1",
                "left program"))
            .ReturnsAsync(true);
        _userIdentityRepository.Setup(repository => repository.GetActiveByManagedUserIdAsync("managed-1"))
            .ReturnsAsync(new UserIdentityLinkRecord {
                ManagedUserId = "managed-1",
                Provider = IdentityProvider.Google,
                ProviderEmail = "rider@example.com",
                ExternalDirectoryObjectId = "external-1"
            });

        var response = await _service.CancelManagedUserAsync(
            "managed-1",
            new CancelManagedUserRequest { ExpectedRowVersion = "rv-1", Reason = "left program" },
            "admin-1");

        Assert.Equal(ManagedUserAccessState.Cancelled, response.Row.ManagedUserAccessState);
        _externalIdentityProvisioningService.Verify(service => service.RevokeAccessAsync("external-1"), Times.Once);
        _userIdentityRepository.Verify(repository => repository.MarkAccessRevokedAsync("managed-1"), Times.Once);
    }
}
