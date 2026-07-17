using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Contracts.Repositories;
using Xunit;

namespace MotorcycleRAG.UnitTests.Services;

public class UserProvisioningServiceTests
{
    private readonly Mock<IUserRepository> _userRepoMock;
    private readonly Mock<IUserIdentityRepository> _userIdentityRepoMock;
    private readonly Mock<IPlanRepository> _planRepoMock;
    private readonly UserProvisioningService _sut;

    public UserProvisioningServiceTests()
    {
        _userRepoMock = new Mock<IUserRepository>();
        _userIdentityRepoMock = new Mock<IUserIdentityRepository>();
        _planRepoMock = new Mock<IPlanRepository>();

        _sut = new UserProvisioningService(_userRepoMock.Object, _userIdentityRepoMock.Object, _planRepoMock.Object, NullLogger<UserProvisioningService>.Instance);
    }

    [Fact]
    public void Constructor_NullDependencies_ThrowsArgumentNullException()
    {
        var logger = NullLogger<UserProvisioningService>.Instance;
        Assert.Throws<ArgumentNullException>(() => new UserProvisioningService(null!, _userIdentityRepoMock.Object, _planRepoMock.Object, logger));
    }

    [Fact]
    public async Task ProvisionOrUpdateUserAsync_ThrowsIfEmailEmpty()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.ProvisionOrUpdateUserAsync("objectId", "", "John", "Doe", "Smith", "google", "sub"));
    }

    [Fact]
    public async Task ProvisionOrUpdateUserAsync_ThrowsIfUserIdEmpty()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.ProvisionOrUpdateUserAsync("", "email@example.com", "John", "Doe", "Smith", "google", "sub"));
    }

    [Fact]
    public async Task ProvisionOrUpdateUserAsync_ThrowsIfAuthProviderEmpty()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.ProvisionOrUpdateUserAsync("userId", "email@example.com", "John", "Doe", "Smith", "", "sub"));
    }

    [Fact]
    public async Task ProvisionOrUpdateUserAsync_UnapprovedUser_ThrowsInvalidOperationException()
    {
        _userIdentityRepoMock.Setup(x => x.GetManagedUserIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IdentityProvider>())).ReturnsAsync((string?)null);
        _userRepoMock.Setup(x => x.GetUserByEmailAsync("email@example.com")).ReturnsAsync((UserDTO?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.ProvisionOrUpdateUserAsync("userId", "email@example.com", "John", "Doe", "Smith", "google", "sub"));
    }

    [Fact]
    public async Task ProvisionOrUpdateUserAsync_ApprovedUser_UpdatesAndReturnsUser()
    {
        var existingUser = new UserDTO { Id = "managedId", Email = "old@example.com", IsEnabled = true, AccessState = ManagedUserAccessState.Active, AuthProvider = "google" };
        _userIdentityRepoMock.Setup(x => x.GetManagedUserIdAsync("", "userId", "email@example.com", IdentityProvider.Google)).ReturnsAsync("managedId");
        _userRepoMock.Setup(x => x.GetUserByIdAsync("managedId")).ReturnsAsync(existingUser);
        _userIdentityRepoMock.Setup(x => x.UpsertAsync(It.IsAny<string>(), It.IsAny<IdentityProvider>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>())).ReturnsAsync(true);

        var result = await _sut.ProvisionOrUpdateUserAsync("userId", "email@example.com", "John D", "John", "Doe", "google", "sub");

        result.Should().Be(existingUser);
        existingUser.Email.Should().Be("email@example.com");
        existingUser.DisplayName.Should().Be("John D");
        _userRepoMock.Verify(x => x.UpdateUserAsync(existingUser), Times.Once);
    }

    [Fact]
    public async Task ProvisionOrUpdateUserAsync_FallbackToEmail_UpdatesAndReturnsUser()
    {
        var existingUser = new UserDTO { Id = "managedId", Email = "email@example.com", IsEnabled = true, AccessState = ManagedUserAccessState.Active, AuthProvider = "google" };
        _userIdentityRepoMock.Setup(x => x.GetManagedUserIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IdentityProvider>())).ReturnsAsync((string?)null);
        _userRepoMock.Setup(x => x.GetUserByEmailAsync("email@example.com")).ReturnsAsync(existingUser);
        _userIdentityRepoMock.Setup(x => x.UpsertAsync(It.IsAny<string>(), It.IsAny<IdentityProvider>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>())).ReturnsAsync(true);

        var result = await _sut.ProvisionOrUpdateUserAsync("userId", "email@example.com", "John D", "John", "Doe", "google", "sub");

        result.Should().Be(existingUser);
        _userRepoMock.Verify(x => x.UpdateUserAsync(existingUser), Times.Once);
    }

    [Fact]
    public async Task ProvisionOrUpdateUserAsync_ProviderMismatch_ThrowsInvalidOperationException()
    {
        var existingUser = new UserDTO { Id = "managedId", Email = "email@example.com", IsEnabled = true, AccessState = ManagedUserAccessState.Active, AuthProvider = "microsoft" };
        _userIdentityRepoMock.Setup(x => x.GetManagedUserIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IdentityProvider>())).ReturnsAsync((string?)null);
        _userRepoMock.Setup(x => x.GetUserByEmailAsync("email@example.com")).ReturnsAsync(existingUser);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.ProvisionOrUpdateUserAsync("userId", "email@example.com", "John D", "John", "Doe", "google", "sub"));
    }

    [Fact]
    public async Task ReconcileApprovedUserAsync_UserDisabled_ReturnsNull()
    {
        var existingUser = new UserDTO { Id = "managedId", Email = "email@example.com", IsEnabled = false, AuthProvider = "google" };
        _userIdentityRepoMock.Setup(x => x.GetManagedUserIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IdentityProvider>())).ReturnsAsync("managedId");
        _userRepoMock.Setup(x => x.GetUserByIdAsync("managedId")).ReturnsAsync(existingUser);

        var result = await _sut.ReconcileApprovedUserAsync("issuer", "subject", "email@example.com", "Name", "First", "Last", IdentityProvider.Google, "providerId", "objectId");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ReconcileApprovedUserAsync_WhenActiveLegacyUserHasNoIdentityLink_PromotesAndReturnsUser()
    {
        var existingUser = new UserDTO
        {
            Id = "managedId",
            Email = "email@example.com",
            IsEnabled = true,
            AccessState = ManagedUserAccessState.None,
            AuthProvider = "Google"
        };
        _userIdentityRepoMock.Setup(x => x.GetManagedUserIdAsync("issuer", "subject", existingUser.Email, IdentityProvider.Google))
            .ReturnsAsync((string?)null);
        _userRepoMock.Setup(x => x.GetUserByEmailAsync(existingUser.Email)).ReturnsAsync(existingUser);
        _userIdentityRepoMock.Setup(x => x.UpsertAsync(
                existingUser.Id,
                IdentityProvider.Google,
                existingUser.Email,
                "issuer",
                "subject",
                "provider-id",
                "object-id"))
            .ReturnsAsync(false);
        _userRepoMock.Setup(x => x.UpdateUserAsync(existingUser)).ReturnsAsync(true);

        var result = await _sut.ReconcileApprovedUserAsync(
            "issuer", "subject", existingUser.Email, null, null, null,
            IdentityProvider.Google, "provider-id", "object-id");

        result.Should().BeSameAs(existingUser);
        result!.AccessState.Should().Be(ManagedUserAccessState.Active);
        _userIdentityRepoMock.Verify(x => x.UpsertAsync(
            existingUser.Id, IdentityProvider.Google, existingUser.Email,
            "issuer", "subject", "provider-id", "object-id"), Times.Once);
        _userRepoMock.Verify(x => x.UpdateUserAsync(existingUser), Times.Exactly(2));
    }

    [Fact]
    public async Task ReconcileApprovedUserAsync_WhenFallbackUserIsCancelled_ReturnsNull()
    {
        var existingUser = new UserDTO
        {
            Id = "managedId",
            Email = "email@example.com",
            IsEnabled = true,
            AccessState = ManagedUserAccessState.Cancelled,
            AuthProvider = "google"
        };
        _userIdentityRepoMock.Setup(x => x.GetManagedUserIdAsync(It.IsAny<string>(), It.IsAny<string>(), existingUser.Email, IdentityProvider.Google))
            .ReturnsAsync((string?)null);
        _userRepoMock.Setup(x => x.GetUserByEmailAsync(existingUser.Email)).ReturnsAsync(existingUser);

        var result = await _sut.ReconcileApprovedUserAsync(
            "issuer", "subject", existingUser.Email, "Name", null, null,
            IdentityProvider.Google, null, null);

        result.Should().BeNull();
        _userIdentityRepoMock.Verify(x => x.UpsertAsync(
            It.IsAny<string>(), It.IsAny<IdentityProvider>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task ResolveManagedUserIdAsync_EmptyEmail_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.ResolveManagedUserIdAsync("issuer", "sub", "", IdentityProvider.Google));
    }
}
