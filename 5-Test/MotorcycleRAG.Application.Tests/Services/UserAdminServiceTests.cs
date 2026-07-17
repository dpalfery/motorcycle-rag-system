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

public class UserAdminServiceTests
{
    private readonly Mock<IUserRepository> _userRepoMock;
    private readonly Mock<IPlanRepository> _planRepoMock;
    private readonly UserAdminService _sut;

    public UserAdminServiceTests()
    {
        _userRepoMock = new Mock<IUserRepository>();
        _planRepoMock = new Mock<IPlanRepository>();
        
        _sut = new UserAdminService(
            _userRepoMock.Object,
            _planRepoMock.Object,
            NullLogger<UserAdminService>.Instance);
    }

    [Fact]
    public void Constructor_NullDependencies_ThrowsArgumentNullException()
    {
        var logger = NullLogger<UserAdminService>.Instance;
        Assert.Throws<ArgumentNullException>(() => new UserAdminService(null!, _planRepoMock.Object, logger));
    }

    [Fact]
    public async Task AssignPlanToUserAsync_ThrowsIfUserOrPlanEmpty()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.AssignPlanToUserAsync("", "planId"));
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.AssignPlanToUserAsync("user", ""));
    }

    [Fact]
    public async Task SetUserEnabledStatusAsync_ThrowsIfUserIdEmpty()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.SetUserEnabledStatusAsync("", true));
    }

    [Fact]
    public async Task SetUserEnabledStatusAsync_UserNotFound_ThrowsArgumentException()
    {
        _userRepoMock.Setup(x => x.GetUserByIdAsync("user1")).ReturnsAsync((UserDTO?)null);

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.SetUserEnabledStatusAsync("user1", true));
    }

    [Fact]
    public async Task SetUserEnabledStatusAsync_StatusSame_ReturnsUser()
    {
        var user = new UserDTO { Id = "user1", IsEnabled = true };
        _userRepoMock.Setup(x => x.GetUserByIdAsync("user1")).ReturnsAsync(user);

        var result = await _sut.SetUserEnabledStatusAsync("user1", true);

        result.Should().Be(user);
        _userRepoMock.Verify(x => x.SetUserEnabledStatusAsync(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task SetUserEnabledStatusAsync_StatusChanges_UpdatesAndReturnsUser()
    {
        var user = new UserDTO { Id = "user1", IsEnabled = false };
        _userRepoMock.Setup(x => x.GetUserByIdAsync("user1")).ReturnsAsync(user);
        _userRepoMock.Setup(x => x.SetUserEnabledStatusAsync("user1", true)).ReturnsAsync(true);

        var result = await _sut.SetUserEnabledStatusAsync("user1", true);

        result.IsEnabled.Should().BeTrue();
        _userRepoMock.Verify(x => x.SetUserEnabledStatusAsync("user1", true), Times.Once);
    }

    [Fact]
    public async Task AssignPlanToUserAsync_UserNotFound_ThrowsArgumentException()
    {
        _userRepoMock.Setup(x => x.GetUserByIdAsync("user1")).ReturnsAsync((UserDTO?)null);

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.AssignPlanToUserAsync("user1", "plan1"));
    }

    [Fact]
    public async Task AssignPlanToUserAsync_PlanNotFound_ThrowsArgumentException()
    {
        var user = new UserDTO { Id = "user1" };
        _userRepoMock.Setup(x => x.GetUserByIdAsync("user1")).ReturnsAsync(user);
        _planRepoMock.Setup(x => x.GetPlanByIdAsync("plan1")).ReturnsAsync((UserPlan?)null);

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.AssignPlanToUserAsync("user1", "plan1"));
    }

    [Fact]
    public async Task AssignPlanToUserAsync_PlanAlreadyAssigned_ReturnsUser()
    {
        var user = new UserDTO { Id = "user1", PlanId = "plan1" };
        var plan = new UserPlan { Id = "plan1" };
        _userRepoMock.Setup(x => x.GetUserByIdAsync("user1")).ReturnsAsync(user);
        _planRepoMock.Setup(x => x.GetPlanByIdAsync("plan1")).ReturnsAsync(plan);

        var result = await _sut.AssignPlanToUserAsync("user1", "plan1");

        result.Should().Be(user);
        _userRepoMock.Verify(x => x.UpdateUserAsync(It.IsAny<UserDTO>()), Times.Never);
    }

    [Fact]
    public async Task AssignPlanToUserAsync_PlanChanged_UpdatesAndReturnsUser()
    {
        var user = new UserDTO { Id = "user1", PlanId = "old_plan" };
        var plan = new UserPlan { Id = "new_plan" };
        _userRepoMock.Setup(x => x.GetUserByIdAsync("user1")).ReturnsAsync(user);
        _planRepoMock.Setup(x => x.GetPlanByIdAsync("new_plan")).ReturnsAsync(plan);
        _userRepoMock.Setup(x => x.UpdateUserAsync(user)).ReturnsAsync(true);

        var result = await _sut.AssignPlanToUserAsync("user1", "new_plan");

        result.PlanId.Should().Be("new_plan");
        _userRepoMock.Verify(x => x.UpdateUserAsync(user), Times.Once);
    }

    [Fact]
    public async Task GetAllUsersAsync_ReturnsUsers()
    {
        var users = new[] { new UserDTO { Id = "user1" } };
        _userRepoMock.Setup(x => x.GetUsersAsync(1, 50)).ReturnsAsync(users);

        var result = await _sut.GetAllUsersAsync();

        result.Should().BeEquivalentTo(users);
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task GetAllUsersAsync_WhenPagingIsOutsideSupportedRange_ThrowsArgumentException(int page, int pageSize)
    {
        var act = () => _sut.GetAllUsersAsync(page, pageSize);

        await act.Should().ThrowAsync<ArgumentException>();
        _userRepoMock.Verify(repository => repository.GetUsersAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }
}
