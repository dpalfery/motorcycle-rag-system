using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using MotorcycleRAG.API.Controllers;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.UnitTests.Presentation.API.Controllers;

public sealed class MeControllerTests
{
    [Fact]
    public void Constructor_NullDependency_Throws() =>
        ((Action)(() => new MeController(null!))).Should().Throw<ArgumentNullException>();

    [Fact]
    public async Task GetProfileAsync_MapsAccessOutcomes()
    {
        var service = new Mock<ICurrentUserProfileService>();
        service.Setup(x => x.GetProfileAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new CurrentUserProfileResult(CurrentUserProfileStatus.Unauthenticated));
        var controller = new MeController(service.Object);
        Status(await controller.GetProfileAsync(), StatusCodes.Status401Unauthorized);

        service.Setup(x => x.GetProfileAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new CurrentUserProfileResult(CurrentUserProfileStatus.Success, new UserProfileResponse { Id = "user" }));
        (await controller.GetProfileAsync()).Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetUsageAsync_MapsDaysToUseCase()
    {
        var service = new Mock<ICurrentUserProfileService>();
        service.Setup(x => x.GetUsageAsync(12, It.IsAny<CancellationToken>())).ReturnsAsync(new CurrentUserUsageResult(CurrentUserProfileStatus.Success, new UsageResponse()));

        (await new MeController(service.Object).GetUsageAsync(12)).Should().BeOfType<OkObjectResult>();
        service.Verify(x => x.GetUsageAsync(12, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateProfileAsync_MapsValidationAndRequest()
    {
        var service = new Mock<ICurrentUserProfileService>();
        var request = new UpdateProfileRequest { DisplayName = "Display" };
        service.Setup(x => x.UpdateProfileAsync(request, It.IsAny<CancellationToken>())).ReturnsAsync(new CurrentUserProfileResult(CurrentUserProfileStatus.ValidationFailed, ValidationErrors: ["invalid"]));
        var controller = new MeController(service.Object);

        Status(await controller.UpdateProfileAsync(request), StatusCodes.Status400BadRequest);
        await controller.Invoking(x => x.UpdateProfileAsync(null!)).Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GetProfileAsync_WhenAccessNotApproved_Returns403()
    {
        var service = new Mock<ICurrentUserProfileService>();
        service.Setup(x => x.GetProfileAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrentUserProfileResult(CurrentUserProfileStatus.AccessNotApproved));
        var controller = new MeController(service.Object);

        Status(await controller.GetProfileAsync(), StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task GetProfileAsync_WhenStatusUnknown_Returns500()
    {
        var service = new Mock<ICurrentUserProfileService>();
        service.Setup(x => x.GetProfileAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrentUserProfileResult((CurrentUserProfileStatus)999));
        var controller = new MeController(service.Object);

        (await controller.GetProfileAsync()).Should().BeOfType<StatusCodeResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task GetUsageAsync_WithoutDays_DefaultsToSevenDayWindow()
    {
        var service = new Mock<ICurrentUserProfileService>();
        service.Setup(x => x.GetUsageAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrentUserUsageResult(CurrentUserProfileStatus.Success, new UsageResponse()));

        (await new MeController(service.Object).GetUsageAsync()).Should().BeOfType<OkObjectResult>();
        service.Verify(x => x.GetUsageAsync(7, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(CurrentUserProfileStatus.Unauthenticated, StatusCodes.Status401Unauthorized)]
    [InlineData(CurrentUserProfileStatus.AccessNotApproved, StatusCodes.Status403Forbidden)]
    public async Task GetUsageAsync_MapsAccessOutcomes(CurrentUserProfileStatus status, int expectedCode)
    {
        var service = new Mock<ICurrentUserProfileService>();
        service.Setup(x => x.GetUsageAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrentUserUsageResult(status));
        var controller = new MeController(service.Object);

        Status(await controller.GetUsageAsync(7), expectedCode);
    }

    [Fact]
    public async Task GetUsageAsync_WhenStatusUnknown_Returns500()
    {
        var service = new Mock<ICurrentUserProfileService>();
        service.Setup(x => x.GetUsageAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrentUserUsageResult((CurrentUserProfileStatus)999));
        var controller = new MeController(service.Object);

        (await controller.GetUsageAsync(7)).Should().BeOfType<StatusCodeResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task UpdateProfileAsync_WhenSuccess_ReturnsOk()
    {
        var service = new Mock<ICurrentUserProfileService>();
        var request = new UpdateProfileRequest { DisplayName = "Display" };
        service.Setup(x => x.UpdateProfileAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrentUserProfileResult(CurrentUserProfileStatus.Success, new UserProfileResponse { Id = "user" }));

        (await new MeController(service.Object).UpdateProfileAsync(request)).Should().BeOfType<OkObjectResult>();
    }

    private static void Status(IActionResult result, int expected) =>
        result.Should().BeAssignableTo<ObjectResult>().Which.StatusCode.Should().Be(expected);
}
