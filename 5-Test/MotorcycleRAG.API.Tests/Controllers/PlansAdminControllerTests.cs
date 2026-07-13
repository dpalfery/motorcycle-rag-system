using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MotorcycleRAG.API.Controllers;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.UnitTests.Presentation.API.Controllers;

public sealed class PlansAdminControllerTests
{
    [Fact]
    public void Constructor_NullDependencies_Throw()
    {
        var service = new Mock<IPlanAdministrationService>();
        ((Action)(() => new PlansAdminController(null!, NullLogger<PlansAdminController>.Instance))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new PlansAdminController(service.Object, null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task GetPlanByIdAsync_ValidatesAndMapsNotFound()
    {
        var service = new Mock<IPlanAdministrationService>();
        service.Setup(x => x.GetPlanByIdAsync("missing", It.IsAny<CancellationToken>())).ReturnsAsync((UserPlan?)null);
        var controller = Create(service.Object);

        Status(await controller.GetPlanByIdAsync(" "), StatusCodes.Status400BadRequest);
        Status(await controller.GetPlanByIdAsync("missing"), StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task CreatePlanAsync_MapsRequestToApplicationCommand()
    {
        var service = new Mock<IPlanAdministrationService>();
        PlanCreateCommand? submitted = null;
        service.Setup(x => x.CreatePlanAsync(It.IsAny<PlanCreateCommand>(), It.IsAny<CancellationToken>()))
            .Callback<PlanCreateCommand, CancellationToken>((command, _) => submitted = command)
            .ReturnsAsync(new UserPlan { Id = "premium" });
        var controller = Create(service.Object);

        var result = await controller.CreatePlanAsync(new CreatePlanRequest { Name = "Premium", DailyRequestLimit = 500, IsPaid = true });

        result.Should().BeOfType<CreatedResult>();
        submitted.Should().Be(new PlanCreateCommand("Premium", string.Empty, 500, true));
    }

    [Fact]
    public async Task UpdateAndDelete_MapsApplicationOutcomes()
    {
        var service = new Mock<IPlanAdministrationService>();
        service.Setup(x => x.UpdatePlanAsync("missing", It.IsAny<PlanUpdateCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync((UserPlan?)null);
        service.Setup(x => x.DeletePlanAsync("missing", It.IsAny<CancellationToken>())).ReturnsAsync(new PlanDeleteResult(false, false));
        var controller = Create(service.Object);

        Status(await controller.UpdatePlanAsync("missing", new UpdatePlanRequest()), StatusCodes.Status404NotFound);
        Status(await controller.DeletePlanAsync("missing"), StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task UpdatePlanAsync_MapsPaidStatusToApplicationCommand()
    {
        var service = new Mock<IPlanAdministrationService>();
        PlanUpdateCommand? submitted = null;
        service.Setup(x => x.UpdatePlanAsync("premium", It.IsAny<PlanUpdateCommand>(), It.IsAny<CancellationToken>()))
            .Callback<string, PlanUpdateCommand, CancellationToken>((_, command, _) => submitted = command)
            .ReturnsAsync(new UserPlan { Id = "premium" });
        var controller = Create(service.Object);

        var result = await controller.UpdatePlanAsync("premium", new UpdatePlanRequest { IsPaid = false });

        result.Should().BeOfType<OkObjectResult>();
        submitted.Should().Be(new PlanUpdateCommand(null, null, null, false));
    }

    private static PlansAdminController Create(IPlanAdministrationService service) =>
        new(service, NullLogger<PlansAdminController>.Instance);

    private static void Status(IActionResult result, int expected) =>
        result.Should().BeAssignableTo<ObjectResult>().Which.StatusCode.Should().Be(expected);
}
