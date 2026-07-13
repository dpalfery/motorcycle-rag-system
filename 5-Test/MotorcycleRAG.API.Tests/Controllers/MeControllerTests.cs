using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MotorcycleRAG.API.Controllers;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.UnitTests.Presentation.API.Controllers;

public sealed class MeControllerTests
{
    [Fact]
    public void Constructor_NullDependencies_Throw()
    {
        var current = new Mock<ICurrentUserService>(); var users = new Mock<IUserRepository>();
        var usage = new Mock<IUsageTrackingService>(); var plans = new Mock<IPlanPolicyService>();
        ((Action)(() => new MeController(null!, users.Object, usage.Object, plans.Object, NullLogger<MeController>.Instance))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new MeController(current.Object, null!, usage.Object, plans.Object, NullLogger<MeController>.Instance))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new MeController(current.Object, users.Object, null!, plans.Object, NullLogger<MeController>.Instance))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new MeController(current.Object, users.Object, usage.Object, null!, NullLogger<MeController>.Instance))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new MeController(current.Object, users.Object, usage.Object, plans.Object, null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task ProfileAndUsage_RejectUnauthenticatedAndUnapprovedUsers()
    {
        var controller = Create(out _, out var current, out _, out _);
        current.SetupGet(x => x.IsAuthenticated).Returns(false);
        Status(await controller.GetProfileAsync(), 401); Status(await controller.GetUsageAsync(), 401);
        current.SetupGet(x => x.IsAuthenticated).Returns(true);
        current.Setup(x => x.GetManagedUserAsync()).ReturnsAsync((UserDTO?)null);
        Status(await controller.GetProfileAsync(), 403); Status(await controller.GetUsageAsync(7), 403);
    }

    [Fact]
    public async Task ProfileAndUsage_ReturnMappedDataAndClampRequestedDays()
    {
        var controller = Authenticated(out _, out var current, out var usage, out var plans);
        var user = User();
        ConfigureUser(current, user);
        plans.Setup(x => x.GetDailyRequestLimitAsync(user)).ReturnsAsync(10);
        plans.Setup(x => x.GetRemainingDailyRequestsAsync(user.Id, null)).ReturnsAsync(7);
        plans.Setup(x => x.GetDailyUsageCountAsync(user.Id, null)).ReturnsAsync(4);
        usage.Setup(x => x.GetUsageByDateRangeAsync(user.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync([new Usage { Id = 1, IsSuccess = true }, new Usage { Id = 2, IsSuccess = false }]);

        (await controller.GetProfileAsync()).Should().BeOfType<OkObjectResult>();
        var low = (await controller.GetUsageAsync(-1)).Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<UsageResponse>().Which;
        low.TotalRequests.Should().Be(2); low.SuccessfulRequests.Should().Be(1); low.FailedRequests.Should().Be(1); low.RemainingDailyRequests.Should().Be(6);
        await controller.GetUsageAsync();
        await controller.GetUsageAsync(31);
        usage.Verify(x => x.GetUsageByDateRangeAsync(user.Id, It.Is<DateTime>(d => d <= DateTime.UtcNow.AddDays(-1).AddSeconds(1)), It.IsAny<DateTime>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task UpdateProfile_ValidatesAuthUserAndRequest()
    {
        var controller = Create(out _, out var current, out _, out _);
        await controller.Invoking(x => x.UpdateProfileAsync(null!)).Should().ThrowAsync<ArgumentNullException>();
        current.SetupGet(x => x.IsAuthenticated).Returns(false);
        Status(await controller.UpdateProfileAsync(new UpdateProfileRequest { DisplayName = "name" }), 401);
        current.SetupGet(x => x.IsAuthenticated).Returns(true);
        current.Setup(x => x.GetManagedUserAsync()).ReturnsAsync((UserDTO?)null);
        Status(await controller.UpdateProfileAsync(new UpdateProfileRequest { DisplayName = "name" }), 403);
        current.Setup(x => x.GetManagedUserAsync()).ReturnsAsync(User());
        Status(await controller.UpdateProfileAsync(new UpdateProfileRequest()), 400);
        Status(await controller.UpdateProfileAsync(new UpdateProfileRequest { DisplayName = new string('x', 101) }), 400);
        Status(await controller.UpdateProfileAsync(new UpdateProfileRequest { FirstName = new string('x', 51) }), 400);
        Status(await controller.UpdateProfileAsync(new UpdateProfileRequest { LastName = new string('x', 51) }), 400);
    }

    [Fact]
    public async Task UpdateProfile_UpdatesChangedFieldsAndReturnsProfile()
    {
        var controller = Authenticated(out var users, out var current, out _, out var plans);
        var user = User(); ConfigureUser(current, user);
        plans.Setup(x => x.GetDailyRequestLimitAsync(user)).ReturnsAsync(10);
        plans.Setup(x => x.GetRemainingDailyRequestsAsync(user.Id, null)).ReturnsAsync(8);
        users.Setup(x => x.UpdateUserAsync(user)).ReturnsAsync(true);
        var result = await controller.UpdateProfileAsync(new UpdateProfileRequest { DisplayName = "New", FirstName = "First", LastName = "Last" });
        result.Should().BeOfType<OkObjectResult>(); users.Verify(x => x.UpdateUserAsync(user), Times.Once);
        var unchanged = await controller.UpdateProfileAsync(new UpdateProfileRequest { DisplayName = "New" });
        unchanged.Should().BeOfType<OkObjectResult>(); users.Verify(x => x.UpdateUserAsync(user), Times.Once);
    }

    private static MeController Authenticated(out Mock<IUserRepository> users, out Mock<ICurrentUserService> current, out Mock<IUsageTrackingService> usage, out Mock<IPlanPolicyService> plans)
    { var c = Create(out users, out current, out usage, out plans); current.SetupGet(x => x.IsAuthenticated).Returns(true); return c; }
    private static MeController Create(out Mock<IUserRepository> users, out Mock<ICurrentUserService> current, out Mock<IUsageTrackingService> usage, out Mock<IPlanPolicyService> plans)
    { users = new(); current = new(); usage = new(); plans = new(); return new MeController(current.Object, users.Object, usage.Object, plans.Object, NullLogger<MeController>.Instance); }
    private static void ConfigureUser(Mock<ICurrentUserService> current, UserDTO user) =>
        current.Setup(x => x.GetManagedUserAsync()).ReturnsAsync(user);
    private static UserDTO User() => new() { Id = "user-1", Email = "u@example.test", DisplayName = "Old", FirstName = "Old", LastName = "Name", PlanId = "basic" };
    private static void Status(IActionResult result, int code) => result.Should().BeAssignableTo<ObjectResult>().Which.StatusCode.Should().Be(code);
}
