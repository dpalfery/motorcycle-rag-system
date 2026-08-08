using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Application.Services.Ingestion;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.UnitTests.Services;

public sealed class ControllerUseCaseServicesTests
{
    [Fact]
    public async Task PlanAdministrationService_CreatesPlanThroughRepository()
    {
        var repository = new Mock<IPlanRepository>();
        repository.Setup(x => x.CreatePlanAsync(It.IsAny<UserPlan>())).ReturnsAsync((UserPlan plan) => plan);
        var service = new PlanAdministrationService(repository.Object, NullLogger<PlanAdministrationService>.Instance);

        var result = await service.CreatePlanAsync(new PlanCreateCommand("Premium", "", 500, true));

        Assert.Equal("Premium", result.Name);
        repository.Verify(x => x.CreatePlanAsync(It.Is<UserPlan>(plan => plan.Name == "Premium" && plan.DailyRequestLimit == 500 && plan.IsPaid)), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PlanAdministrationService_UpdatesPaidStatusWhenSupplied(bool isPaid)
    {
        var plan = new UserPlan { Id = "premium", IsPaid = !isPaid };
        var repository = new Mock<IPlanRepository>();
        repository.Setup(x => x.GetPlanByIdAsync("premium")).ReturnsAsync(plan);
        repository.Setup(x => x.UpdatePlanAsync(plan)).ReturnsAsync(true);
        var service = new PlanAdministrationService(repository.Object, NullLogger<PlanAdministrationService>.Instance);

        var result = await service.UpdatePlanAsync("premium", new PlanUpdateCommand(null, null, null, isPaid));

        result!.IsPaid.Should().Be(isPaid);
        repository.Verify(x => x.UpdatePlanAsync(It.Is<UserPlan>(updated => updated.IsPaid == isPaid)), Times.Once);
    }

    [Fact]
    public async Task PlanAdministrationService_PreservesPaidStatusWhenOmitted()
    {
        var plan = new UserPlan { Id = "premium", IsPaid = true };
        var repository = new Mock<IPlanRepository>();
        repository.Setup(x => x.GetPlanByIdAsync("premium")).ReturnsAsync(plan);
        repository.Setup(x => x.UpdatePlanAsync(plan)).ReturnsAsync(true);
        var service = new PlanAdministrationService(repository.Object, NullLogger<PlanAdministrationService>.Instance);

        var result = await service.UpdatePlanAsync("premium", new PlanUpdateCommand(null, null, null, null));

        result!.IsPaid.Should().BeTrue();
        repository.Verify(x => x.UpdatePlanAsync(It.Is<UserPlan>(updated => updated.IsPaid)), Times.Once);
    }

    [Fact]
    public async Task CurrentUserProfileService_UpdatesOnlyChangedProfileFields()
    {
        var currentUser = new Mock<ICurrentUserService>();
        var user = new UserDTO { Id = "user", DisplayName = "Old" };
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.Setup(x => x.GetManagedUserAsync()).ReturnsAsync(user);
        var users = new Mock<IUserRepository>();
        users.Setup(x => x.UpdateUserAsync(user)).ReturnsAsync(true);
        var plans = new Mock<IPlanPolicyService>();
        plans.Setup(x => x.GetDailyRequestLimitAsync(user)).ReturnsAsync(10);
        plans.Setup(x => x.GetRemainingDailyRequestsAsync(user.Id, null)).ReturnsAsync(10);
        var service = new CurrentUserProfileService(currentUser.Object, users.Object, Mock.Of<IUsageTrackingService>(), plans.Object, NullLogger<CurrentUserProfileService>.Instance);

        var result = await service.UpdateProfileAsync(new UpdateProfileRequest { DisplayName = "New" });

        Assert.Equal(CurrentUserProfileStatus.Success, result.Status);
        users.Verify(x => x.UpdateUserAsync(user), Times.Once);
    }

    [Fact]
    public async Task ProcessorArtifactService_ValidatesAccessTokenBeforeStorageAccess()
    {
        var tokenService = new Mock<IIngestionSourceAccessTokenService>();
        tokenService.Setup(x => x.IsValid("invalid", "00000000-0000-0000-0000-000000000001", "manual-pdf")).Returns(false);
        var blobStorage = new Mock<IBlobStorageService>();
        var service = new ProcessorArtifactService(
            blobStorage.Object,
            Options.Create(new BlobStorageOptions { RawUploadsContainer = "raw-uploads" }),
            Mock.Of<ISearchChunkIndexingCoordinator>(),
            Mock.Of<IIngestionJobRepository>(),
            tokenService.Object,
            Mock.Of<IIngestionJobService>(),
            NullLogger<ProcessorArtifactService>.Instance);

        var result = await service.DownloadSourceAsync("00000000-0000-0000-0000-000000000001", "manual-pdf", "invalid");

        Assert.Equal(ProcessorArtifactOperationStatus.Unauthorized, result.Status);
        blobStorage.Verify(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
