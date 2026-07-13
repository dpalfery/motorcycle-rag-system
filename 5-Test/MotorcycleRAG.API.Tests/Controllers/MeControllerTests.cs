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

    private static void Status(IActionResult result, int expected) =>
        result.Should().BeAssignableTo<ObjectResult>().Which.StatusCode.Should().Be(expected);
}
