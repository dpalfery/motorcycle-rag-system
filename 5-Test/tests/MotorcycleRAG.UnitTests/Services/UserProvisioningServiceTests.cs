using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.UnitTests.Services;

public class UserProvisioningServiceTests {
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IUserIdentityRepository> _userIdentityRepository = new();
    private readonly Mock<IPlanRepository> _planRepository = new();
    private readonly Mock<ILogger<UserProvisioningService>> _logger = new();

    [Fact]
    public async Task ReconcileApprovedUserAsync_WhenEmailMatchesDifferentProvider_ReturnsNull() {
        var user = new UserDTO {
            Id = "user-1",
            Email = "rider@example.com",
            AuthProvider = "Google",
            IsEnabled = true,
            AccessState = ManagedUserAccessState.Active,
            ProviderUserId = "google-subject"
        };

        _userIdentityRepository
            .Setup(repository => repository.GetManagedUserIdAsync("issuer", "subject", "rider@example.com", IdentityProvider.Microsoft))
            .ReturnsAsync((string?)null);
        _userRepository
            .Setup(repository => repository.GetUserByEmailAsync("rider@example.com"))
            .ReturnsAsync(user);

        var service = CreateService();

        var result = await service.ReconcileApprovedUserAsync(
            "issuer",
            "subject",
            "rider@example.com",
            "Rider",
            "Road",
            "Runner",
            IdentityProvider.Microsoft,
            "oid-1",
            "object-id");

        result.Should().BeNull();
        _userIdentityRepository.Verify(
            repository => repository.UpsertAsync(It.IsAny<string>(), It.IsAny<IdentityProvider>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Never);
        _userRepository.Verify(repository => repository.UpdateUserAsync(It.IsAny<UserDTO>()), Times.Never);
    }

    [Fact]
    public async Task ReconcileApprovedUserAsync_WhenSameProviderFallbackExists_UpdatesUserAndIdentityLink() {
        var user = new UserDTO {
            Id = "user-1",
            Email = "rider@example.com",
            DisplayName = "Old Rider",
            FirstName = "Old",
            LastName = "Name",
            AuthProvider = "Google",
            IsEnabled = true,
            AccessState = ManagedUserAccessState.None,
            ProviderUserId = "old-provider-id"
        };

        _userIdentityRepository
            .Setup(repository => repository.GetManagedUserIdAsync("issuer", "subject", "rider@example.com", IdentityProvider.Google))
            .ReturnsAsync((string?)null);
        _userRepository
            .Setup(repository => repository.GetUserByEmailAsync("rider@example.com"))
            .ReturnsAsync(user);
        _userIdentityRepository
            .Setup(repository => repository.UpsertAsync("user-1", IdentityProvider.Google, "rider@example.com", "issuer", "subject", "new-provider-id", "object-id"))
            .ReturnsAsync(true);
        _userRepository
            .Setup(repository => repository.UpdateUserAsync(It.IsAny<UserDTO>()))
            .ReturnsAsync(true);

        var service = CreateService();

        var result = await service.ReconcileApprovedUserAsync(
            "issuer",
            "subject",
            "rider@example.com",
            "New Rider",
            "Road",
            "Runner",
            IdentityProvider.Google,
            "new-provider-id",
            "object-id");

        result.Should().NotBeNull();
        result!.Id.Should().Be("user-1");
        result.DisplayName.Should().Be("New Rider");
        result.AccessState.Should().Be(ManagedUserAccessState.Active);
        result.ProviderUserId.Should().Be("new-provider-id");

        _userIdentityRepository.Verify(
            repository => repository.UpsertAsync("user-1", IdentityProvider.Google, "rider@example.com", "issuer", "subject", "new-provider-id", "object-id"),
            Times.Once);
        _userRepository.Verify(repository => repository.UpdateUserAsync(It.Is<UserDTO>(updatedUser => updatedUser.Id == "user-1")), Times.AtLeastOnce);
    }

    private UserProvisioningService CreateService() {
        return new UserProvisioningService(
            _userRepository.Object,
            _userIdentityRepository.Object,
            _planRepository.Object,
            _logger.Object);
    }
}