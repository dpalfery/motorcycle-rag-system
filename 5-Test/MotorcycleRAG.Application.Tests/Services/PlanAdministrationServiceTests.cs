using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.UnitTests.Services;

public sealed class PlanAdministrationServiceTests
{
    private readonly Mock<IPlanRepository> _planRepository = new();

    private PlanAdministrationService CreateSut() =>
        new(_planRepository.Object, NullLogger<PlanAdministrationService>.Instance);

    [Fact]
    public void Constructor_NullRepository_ThrowsArgumentNullException()
    {
        var act = () => new PlanAdministrationService(null!, NullLogger<PlanAdministrationService>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("planRepository");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new PlanAdministrationService(_planRepository.Object, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task GetAllPlansAsync_ReturnsPlansFromRepository()
    {
        var plans = new[] { new UserPlan { Id = "1", Name = "Free" }, new UserPlan { Id = "2", Name = "Pro" } };
        _planRepository.Setup(r => r.GetAllPlansAsync()).ReturnsAsync(plans);
        var sut = CreateSut();

        var result = await sut.GetAllPlansAsync();

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetPlanByIdAsync_WhenFound_ReturnsPlan()
    {
        var plan = new UserPlan { Id = "pro", Name = "Pro" };
        _planRepository.Setup(r => r.GetPlanByIdAsync("pro")).ReturnsAsync(plan);
        var sut = CreateSut();

        var result = await sut.GetPlanByIdAsync("pro");

        result.Should().Be(plan);
    }

    [Fact]
    public async Task GetPlanByIdAsync_WhenNotFound_ReturnsNull()
    {
        _planRepository.Setup(r => r.GetPlanByIdAsync("missing")).ReturnsAsync((UserPlan?)null);
        var sut = CreateSut();

        var result = await sut.GetPlanByIdAsync("missing");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetPlanByIdAsync_EmptyId_ThrowsArgumentException()
    {
        var sut = CreateSut();
        var act = () => sut.GetPlanByIdAsync("  ");
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("planId");
    }

    [Fact]
    public async Task CreatePlanAsync_NullCommand_ThrowsArgumentNullException()
    {
        var sut = CreateSut();
        var act = () => sut.CreatePlanAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("command");
    }

    [Fact]
    public async Task UpdatePlanAsync_UpdatesNameDescriptionAndDailyLimit()
    {
        var plan = new UserPlan { Id = "pro", Name = "Old", Description = "Old desc", DailyRequestLimit = 10, IsPaid = false };
        _planRepository.Setup(r => r.GetPlanByIdAsync("pro")).ReturnsAsync(plan);
        _planRepository.Setup(r => r.UpdatePlanAsync(plan)).ReturnsAsync(true);
        var sut = CreateSut();

        var result = await sut.UpdatePlanAsync("pro", new PlanUpdateCommand("New", "New desc", 500, null));

        result.Should().NotBeNull();
        result!.Name.Should().Be("New");
        result.Description.Should().Be("New desc");
        result.DailyRequestLimit.Should().Be(500);
        result.IsPaid.Should().BeFalse();
    }

    [Fact]
    public async Task UpdatePlanAsync_WhenUpdateFails_ThrowsInvalidOperationException()
    {
        var plan = new UserPlan { Id = "pro" };
        _planRepository.Setup(r => r.GetPlanByIdAsync("pro")).ReturnsAsync(plan);
        _planRepository.Setup(r => r.UpdatePlanAsync(plan)).ReturnsAsync(false);
        var sut = CreateSut();

        var act = () => sut.UpdatePlanAsync("pro", new PlanUpdateCommand("X", null, null, null));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Failed to update plan*");
    }

    [Fact]
    public async Task DeletePlanAsync_WhenNotFound_ReturnsNotFoundNotDeleted()
    {
        _planRepository.Setup(r => r.GetPlanByIdAsync("missing")).ReturnsAsync((UserPlan?)null);
        var sut = CreateSut();

        var result = await sut.DeletePlanAsync("missing");

        result.Should().Be(new PlanDeleteResult(false, false));
        _planRepository.Verify(r => r.DeletePlanAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task DeletePlanAsync_WhenFoundAndDeleted_ReturnsFoundAndDeleted()
    {
        _planRepository.Setup(r => r.GetPlanByIdAsync("pro")).ReturnsAsync(new UserPlan { Id = "pro" });
        _planRepository.Setup(r => r.DeletePlanAsync("pro")).ReturnsAsync(true);
        var sut = CreateSut();

        var result = await sut.DeletePlanAsync("pro");

        result.Should().Be(new PlanDeleteResult(true, true));
    }

    [Fact]
    public async Task DeletePlanAsync_WhenFoundButDeleteFails_ReturnsFoundNotDeleted()
    {
        _planRepository.Setup(r => r.GetPlanByIdAsync("pro")).ReturnsAsync(new UserPlan { Id = "pro" });
        _planRepository.Setup(r => r.DeletePlanAsync("pro")).ReturnsAsync(false);
        var sut = CreateSut();

        var result = await sut.DeletePlanAsync("pro");

        result.Should().Be(new PlanDeleteResult(true, false));
    }

    [Fact]
    public async Task DeletePlanAsync_EmptyId_ThrowsArgumentException()
    {
        var sut = CreateSut();
        var act = () => sut.DeletePlanAsync("  ");
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("planId");
    }
}
