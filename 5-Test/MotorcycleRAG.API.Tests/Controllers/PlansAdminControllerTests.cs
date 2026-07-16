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
        Assert.Throws<ArgumentNullException>(() => new PlansAdminController(null!, NullLogger<PlansAdminController>.Instance));
        Assert.Throws<ArgumentNullException>(() => new PlansAdminController(service.Object, null!));
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

    [Fact]
    public async Task GetAllPlansAsync_HappyAndErrorPaths()
    {
        var service = new Mock<IPlanAdministrationService>();
        service.Setup(x => x.GetAllPlansAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new UserPlan { Id = "free" } });
        var controller = Create(service.Object);

        (await controller.GetAllPlansAsync()).Should().BeOfType<OkObjectResult>();

        service.Setup(x => x.GetAllPlansAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException());
        Status(await controller.GetAllPlansAsync(), StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task GetPlanByIdAsync_FoundReturnsOkAndErrorsMapTo500()
    {
        var service = new Mock<IPlanAdministrationService>();
        service.Setup(x => x.GetPlanByIdAsync("free", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserPlan { Id = "free" });
        var controller = Create(service.Object);

        (await controller.GetPlanByIdAsync("free")).Should().BeOfType<OkObjectResult>();

        service.Setup(x => x.GetPlanByIdAsync("boom", It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException());
        Status(await controller.GetPlanByIdAsync("boom"), StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task CreatePlanAsync_NullRequestAndValidationAndErrorBranches()
    {
        var service = new Mock<IPlanAdministrationService>();
        service.Setup(x => x.CreatePlanAsync(It.IsAny<PlanCreateCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserPlan { Id = "x" });
        var controller = Create(service.Object);

        Status(await controller.CreatePlanAsync(null), StatusCodes.Status400BadRequest);
        Status(await controller.CreatePlanAsync(new CreatePlanRequest { Name = " ", DailyRequestLimit = 100 }), StatusCodes.Status400BadRequest);
        Status(await controller.CreatePlanAsync(new CreatePlanRequest { Name = new string('n', 101), DailyRequestLimit = 100 }), StatusCodes.Status400BadRequest);
        Status(await controller.CreatePlanAsync(new CreatePlanRequest { Name = "ok", DailyRequestLimit = 0 }), StatusCodes.Status400BadRequest);
        Status(await controller.CreatePlanAsync(new CreatePlanRequest { Name = "ok", DailyRequestLimit = 10001 }), StatusCodes.Status400BadRequest);

        service.Setup(x => x.CreatePlanAsync(It.IsAny<PlanCreateCommand>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException());
        Status(await controller.CreatePlanAsync(new CreatePlanRequest { Name = "ok", DailyRequestLimit = 100 }), StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task UpdatePlanAsync_GuardAndValidationAndErrorBranches()
    {
        var service = new Mock<IPlanAdministrationService>();
        service.Setup(x => x.UpdatePlanAsync(It.IsAny<string>(), It.IsAny<PlanUpdateCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserPlan { Id = "free" });
        var controller = Create(service.Object);

        Status(await controller.UpdatePlanAsync(" ", new UpdatePlanRequest()), StatusCodes.Status400BadRequest);
        Status(await controller.UpdatePlanAsync("free", null), StatusCodes.Status400BadRequest);
        Status(await controller.UpdatePlanAsync("free", new UpdatePlanRequest { Name = new string('n', 101) }), StatusCodes.Status400BadRequest);
        Status(await controller.UpdatePlanAsync("free", new UpdatePlanRequest { DailyRequestLimit = 0 }), StatusCodes.Status400BadRequest);

        service.Setup(x => x.UpdatePlanAsync(It.IsAny<string>(), It.IsAny<PlanUpdateCommand>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException());
        Status(await controller.UpdatePlanAsync("free", new UpdatePlanRequest()), StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task DeletePlanAsync_GuardAndFoundNotDeletedAndErrorBranches()
    {
        var service = new Mock<IPlanAdministrationService>();
        var controller = Create(service.Object);

        Status(await controller.DeletePlanAsync(" "), StatusCodes.Status400BadRequest);

        service.Setup(x => x.DeletePlanAsync("stuck", It.IsAny<CancellationToken>())).ReturnsAsync(new PlanDeleteResult(true, false));
        Status(await controller.DeletePlanAsync("stuck"), StatusCodes.Status500InternalServerError);

        service.Setup(x => x.DeletePlanAsync("gone", It.IsAny<CancellationToken>())).ReturnsAsync(new PlanDeleteResult(true, true));
        (await controller.DeletePlanAsync("gone")).Should().BeOfType<NoContentResult>();

        service.Setup(x => x.DeletePlanAsync("boom", It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException());
        Status(await controller.DeletePlanAsync("boom"), StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public void Validate_CreatePlanRequestNull_ReturnsRequestBodyRequired()
    {
        var method = typeof(PlansAdminController).GetMethod(
            "Validate",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static,
            binder: null,
            types: new[] { typeof(CreatePlanRequest) },
            modifiers: null);
        method.Should().NotBeNull();

        var errors = (List<string>)method!.Invoke(null, new object?[] { null })!;

        errors.Should().ContainSingle().Which.Should().Be("Request body is required");
    }

    [Fact]
    public void Validate_UpdatePlanRequestNull_ReturnsRequestBodyRequired()
    {
        var method = typeof(PlansAdminController).GetMethod(
            "Validate",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static,
            binder: null,
            types: new[] { typeof(UpdatePlanRequest) },
            modifiers: null);
        method.Should().NotBeNull();

        var errors = (List<string>)method!.Invoke(null, new object?[] { null })!;

        errors.Should().ContainSingle().Which.Should().Be("Request body is required");
    }

    private static PlansAdminController Create(IPlanAdministrationService service) =>
        new(service, NullLogger<PlansAdminController>.Instance);

    private static void Status(IActionResult result, int expected) =>
        result.Should().BeAssignableTo<ObjectResult>().Which.StatusCode.Should().Be(expected);
}
