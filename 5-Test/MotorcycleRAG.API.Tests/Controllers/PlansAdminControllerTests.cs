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
    public void Constructor_WithNullDependencies_ThrowsArgumentNullException()
    {
        var plans = new Mock<IPlanRepository>();
        var users = new Mock<IUserAdminService>();

        ((Action)(() => new PlansAdminController(null!, users.Object, NullLogger<PlansAdminController>.Instance))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new PlansAdminController(plans.Object, null!, NullLogger<PlansAdminController>.Instance))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new PlansAdminController(plans.Object, users.Object, null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task GetAllPlansAsync_ReturnsPlansOrSafeFailure()
    {
        var controller = CreateController(out var repository);
        var plans = new[] { Plan("basic") };
        repository.Setup(service => service.GetAllPlansAsync()).ReturnsAsync(plans);

        var success = await controller.GetAllPlansAsync();

        success.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(plans);

        repository.Setup(service => service.GetAllPlansAsync()).ThrowsAsync(new InvalidOperationException("database detail"));
        var failure = await controller.GetAllPlansAsync();
        AssertStatus(failure, StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task GetPlanByIdAsync_ValidatesMissingNotFoundSuccessAndFailure()
    {
        var controller = CreateController(out var repository);

        AssertStatus(await controller.GetPlanByIdAsync(" "), StatusCodes.Status400BadRequest);

        repository.Setup(service => service.GetPlanByIdAsync("missing")).ReturnsAsync((UserPlan?)null);
        AssertStatus(await controller.GetPlanByIdAsync("missing"), StatusCodes.Status404NotFound);

        var plan = Plan("basic");
        repository.Setup(service => service.GetPlanByIdAsync("basic")).ReturnsAsync(plan);
        (await controller.GetPlanByIdAsync("basic")).Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(plan);

        repository.Setup(service => service.GetPlanByIdAsync("broken")).ThrowsAsync(new InvalidOperationException("database detail"));
        AssertStatus(await controller.GetPlanByIdAsync("broken"), StatusCodes.Status500InternalServerError);
    }

    [Theory]
    [MemberData(nameof(InvalidCreateRequests))]
    public async Task CreatePlanAsync_WithInvalidRequest_ReturnsBadRequest(CreatePlanRequest? request)
    {
        var controller = CreateController(out var repository);

        var result = await controller.CreatePlanAsync(request!);

        AssertStatus(result, StatusCodes.Status400BadRequest);
        repository.Verify(service => service.CreatePlanAsync(It.IsAny<UserPlan>()), Times.Never);
    }

    [Fact]
    public async Task CreatePlanAsync_CreatesPlanWithRequestValuesOrSafeFailure()
    {
        var controller = CreateController(out var repository);
        var request = new CreatePlanRequest { Name = "Premium", Description = null, DailyRequestLimit = 500, IsPaid = true };
        UserPlan? submitted = null;
        repository.Setup(service => service.CreatePlanAsync(It.IsAny<UserPlan>()))
            .Callback<UserPlan>(plan => submitted = plan)
            .ReturnsAsync((UserPlan plan) => plan);

        var result = await controller.CreatePlanAsync(request);

        result.Should().BeOfType<CreatedResult>().Which.Value.Should().BeSameAs(submitted);
        submitted.Should().Match<UserPlan>(plan =>
            plan.Name == "Premium" && plan.Description == string.Empty && plan.DailyRequestLimit == 500 && plan.IsPaid);

        repository.Setup(service => service.CreatePlanAsync(It.IsAny<UserPlan>())).ThrowsAsync(new InvalidOperationException("database detail"));
        AssertStatus(await controller.CreatePlanAsync(request), StatusCodes.Status500InternalServerError);
    }

    [Theory]
    [MemberData(nameof(InvalidUpdateArguments))]
    public async Task UpdatePlanAsync_WithInvalidArguments_ReturnsBadRequest(string planId, UpdatePlanRequest? request)
    {
        var controller = CreateController(out var repository);

        var result = await controller.UpdatePlanAsync(planId, request!);

        AssertStatus(result, StatusCodes.Status400BadRequest);
        repository.Verify(service => service.GetPlanByIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UpdatePlanAsync_HandlesMissingFailedSuccessAndRepositoryException()
    {
        var controller = CreateController(out var repository);
        var request = new UpdatePlanRequest { Name = "Renamed", Description = "Updated", DailyRequestLimit = 200 };

        repository.Setup(service => service.GetPlanByIdAsync("missing")).ReturnsAsync((UserPlan?)null);
        AssertStatus(await controller.UpdatePlanAsync("missing", request), StatusCodes.Status404NotFound);

        var plan = Plan("basic");
        repository.Setup(service => service.GetPlanByIdAsync("basic")).ReturnsAsync(plan);
        repository.Setup(service => service.UpdatePlanAsync(plan)).ReturnsAsync(false);
        AssertStatus(await controller.UpdatePlanAsync("basic", request), StatusCodes.Status500InternalServerError);

        repository.Setup(service => service.UpdatePlanAsync(plan)).ReturnsAsync(true);
        var success = await controller.UpdatePlanAsync("basic", request);
        success.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(plan);
        plan.Should().Match<UserPlan>(value => value.Name == "Renamed" && value.Description == "Updated" && value.DailyRequestLimit == 200);

        repository.Setup(service => service.GetPlanByIdAsync("broken")).ThrowsAsync(new InvalidOperationException("database detail"));
        AssertStatus(await controller.UpdatePlanAsync("broken", request), StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task DeletePlanAsync_HandlesInvalidMissingFailureSuccessAndRepositoryException()
    {
        var controller = CreateController(out var repository);

        AssertStatus(await controller.DeletePlanAsync(" "), StatusCodes.Status400BadRequest);

        repository.Setup(service => service.GetPlanByIdAsync("missing")).ReturnsAsync((UserPlan?)null);
        AssertStatus(await controller.DeletePlanAsync("missing"), StatusCodes.Status404NotFound);

        repository.Setup(service => service.GetPlanByIdAsync("basic")).ReturnsAsync(Plan("basic"));
        repository.Setup(service => service.DeletePlanAsync("basic")).ReturnsAsync(false);
        AssertStatus(await controller.DeletePlanAsync("basic"), StatusCodes.Status500InternalServerError);

        repository.Setup(service => service.DeletePlanAsync("basic")).ReturnsAsync(true);
        (await controller.DeletePlanAsync("basic")).Should().BeOfType<NoContentResult>();

        repository.Setup(service => service.GetPlanByIdAsync("broken")).ThrowsAsync(new InvalidOperationException("database detail"));
        AssertStatus(await controller.DeletePlanAsync("broken"), StatusCodes.Status500InternalServerError);
    }

    public static IEnumerable<object[]> InvalidCreateRequests()
    {
        yield return [null!];
        yield return [new CreatePlanRequest { Name = "", DailyRequestLimit = 10 }];
        yield return [new CreatePlanRequest { Name = new string('x', 101), DailyRequestLimit = 10 }];
        yield return [new CreatePlanRequest { Name = "basic", DailyRequestLimit = 0 }];
        yield return [new CreatePlanRequest { Name = "basic", DailyRequestLimit = 10_001 }];
    }

    public static IEnumerable<object[]> InvalidUpdateArguments()
    {
        yield return ["", new UpdatePlanRequest()];
        yield return ["basic", null!];
        yield return ["basic", new UpdatePlanRequest { Name = new string('x', 101) }];
        yield return ["basic", new UpdatePlanRequest { DailyRequestLimit = 0 }];
        yield return ["basic", new UpdatePlanRequest { DailyRequestLimit = 10_001 }];
    }

    private static PlansAdminController CreateController(out Mock<IPlanRepository> repository)
    {
        repository = new Mock<IPlanRepository>(MockBehavior.Loose);
        return new PlansAdminController(repository.Object, new Mock<IUserAdminService>().Object, NullLogger<PlansAdminController>.Instance);
    }

    private static UserPlan Plan(string id) => new() { Id = id, Name = id, Description = "original", DailyRequestLimit = 100 };

    private static void AssertStatus(IActionResult result, int expectedStatus) =>
        result.Should().BeAssignableTo<ObjectResult>().Which.StatusCode.Should().Be(expectedStatus);
}
