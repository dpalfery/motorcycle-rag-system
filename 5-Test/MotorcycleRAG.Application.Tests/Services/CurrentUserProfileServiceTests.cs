using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.UnitTests.Services;

public sealed class CurrentUserProfileServiceTests
{
    private readonly Mock<ICurrentUserService> _currentUserService;
    private readonly Mock<IUserRepository> _userRepository;
    private readonly Mock<IUsageTrackingService> _usageTrackingService;
    private readonly Mock<IPlanPolicyService> _planPolicyService;
    private readonly CurrentUserProfileService _service;

    public CurrentUserProfileServiceTests()
    {
        _currentUserService = new Mock<ICurrentUserService>();
        _userRepository = new Mock<IUserRepository>();
        _usageTrackingService = new Mock<IUsageTrackingService>();
        _planPolicyService = new Mock<IPlanPolicyService>();

        _service = new CurrentUserProfileService(
            _currentUserService.Object,
            _userRepository.Object,
            _usageTrackingService.Object,
            _planPolicyService.Object,
            NullLogger<CurrentUserProfileService>.Instance);
    }

    #region Constructor

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenCurrentUserServiceIsNull()
    {
        var action = () => new CurrentUserProfileService(
            null!,
            _userRepository.Object,
            _usageTrackingService.Object,
            _planPolicyService.Object,
            NullLogger<CurrentUserProfileService>.Instance);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenUserRepositoryIsNull()
    {
        var action = () => new CurrentUserProfileService(
            _currentUserService.Object,
            null!,
            _usageTrackingService.Object,
            _planPolicyService.Object,
            NullLogger<CurrentUserProfileService>.Instance);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenUsageTrackingServiceIsNull()
    {
        var action = () => new CurrentUserProfileService(
            _currentUserService.Object,
            _userRepository.Object,
            null!,
            _planPolicyService.Object,
            NullLogger<CurrentUserProfileService>.Instance);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenPlanPolicyServiceIsNull()
    {
        var action = () => new CurrentUserProfileService(
            _currentUserService.Object,
            _userRepository.Object,
            _usageTrackingService.Object,
            null!,
            NullLogger<CurrentUserProfileService>.Instance);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenLoggerIsNull()
    {
        var action = () => new CurrentUserProfileService(
            _currentUserService.Object,
            _userRepository.Object,
            _usageTrackingService.Object,
            _planPolicyService.Object,
            null!);

        action.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region GetProfileAsync

    [Fact]
    public async Task GetProfileAsync_WhenNotAuthenticated_ReturnsUnauthenticated()
    {
        _currentUserService.SetupGet(x => x.IsAuthenticated).Returns(false);

        var result = await _service.GetProfileAsync();

        result.Status.Should().Be(CurrentUserProfileStatus.Unauthenticated);
        result.Profile.Should().BeNull();
    }

    [Fact]
    public async Task GetProfileAsync_WhenAccessNotApproved_ReturnsAccessNotApproved()
    {
        _currentUserService.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUserService.Setup(x => x.GetManagedUserAsync()).ReturnsAsync((UserDTO?)null);

        var result = await _service.GetProfileAsync();

        result.Status.Should().Be(CurrentUserProfileStatus.AccessNotApproved);
        result.Profile.Should().BeNull();
    }

    [Fact]
    public async Task GetProfileAsync_WhenAuthenticated_ReturnsProfile()
    {
        var user = CreateUser();
        SetupAuthenticatedUser(user);
        _planPolicyService.Setup(x => x.GetDailyRequestLimitAsync(user)).ReturnsAsync(10);
        _planPolicyService.Setup(x => x.GetRemainingDailyRequestsAsync(user.Id, null)).ReturnsAsync(5);

        var result = await _service.GetProfileAsync();

        result.Status.Should().Be(CurrentUserProfileStatus.Success);
        result.Profile.Should().NotBeNull();
        result.Profile!.Id.Should().Be(user.Id);
        result.Profile.Email.Should().Be(user.Email);
        result.Profile.DisplayName.Should().Be(user.DisplayName);
        result.Profile.FirstName.Should().Be(user.FirstName);
        result.Profile.LastName.Should().Be(user.LastName);
        result.Profile.IsEnabled.Should().Be(user.IsEnabled);
        result.Profile.CreatedDate.Should().Be(user.CreatedDate);
        result.Profile.LastUpdatedDate.Should().Be(user.LastUpdatedDate);
        result.Profile.PlanId.Should().Be(user.PlanId);
        result.Profile.DailyRequestLimit.Should().Be(10);
        result.Profile.RemainingDailyRequests.Should().Be(5);
    }

    #endregion

    #region GetUsageAsync

    [Fact]
    public async Task GetUsageAsync_WhenNotAuthenticated_ReturnsUnauthenticated()
    {
        _currentUserService.SetupGet(x => x.IsAuthenticated).Returns(false);

        var result = await _service.GetUsageAsync(7);

        result.Status.Should().Be(CurrentUserProfileStatus.Unauthenticated);
        result.Usage.Should().BeNull();
    }

    [Fact]
    public async Task GetUsageAsync_WhenAccessNotApproved_ReturnsAccessNotApproved()
    {
        _currentUserService.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUserService.Setup(x => x.GetManagedUserAsync()).ReturnsAsync((UserDTO?)null);

        var result = await _service.GetUsageAsync(7);

        result.Status.Should().Be(CurrentUserProfileStatus.AccessNotApproved);
        result.Usage.Should().BeNull();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(15, 15)]
    [InlineData(30, 30)]
    [InlineData(31, 30)]
    [InlineData(100, 30)]
    public async Task GetUsageAsync_ClampDaysToRange1To30(int inputDays, int expectedDays)
    {
        var user = CreateUser();
        SetupAuthenticatedUser(user);
        var usage = new[]
        {
            new Usage { Id = 1, IsSuccess = true },
            new Usage { Id = 2, IsSuccess = false },
            new Usage { Id = 3, IsSuccess = true }
        };
        _usageTrackingService
            .Setup(x => x.GetUsageByDateRangeAsync(user.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(usage);
        _planPolicyService.Setup(x => x.GetDailyUsageCountAsync(user.Id, null)).ReturnsAsync(2);
        _planPolicyService.Setup(x => x.GetDailyRequestLimitAsync(user)).ReturnsAsync(10);

        var result = await _service.GetUsageAsync(inputDays);

        result.Status.Should().Be(CurrentUserProfileStatus.Success);
        result.Usage.Should().NotBeNull();
        result.Usage!.TotalRequests.Should().Be(3);
        result.Usage.SuccessfulRequests.Should().Be(2);
        result.Usage.FailedRequests.Should().Be(1);
        result.Usage.DailyUsageCount.Should().Be(2);
        result.Usage.DailyRequestLimit.Should().Be(10);
        result.Usage.RemainingDailyRequests.Should().Be(8);
        (result.Usage.EndDate - result.Usage.StartDate).TotalDays.Should().Be(expectedDays);
        result.Usage.UsageRecords.Should().HaveCount(3);

        _usageTrackingService.Verify(
            x => x.GetUsageByDateRangeAsync(user.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>()),
            Times.Once);
    }

    [Fact]
    public async Task GetUsageAsync_MapsUsageRecordsCorrectly()
    {
        var user = CreateUser();
        SetupAuthenticatedUser(user);
        var now = DateTime.UtcNow;
        var usage = new[]
        {
            new Usage
            {
                Id = 1,
                Endpoint = "/api/test",
                HttpMethod = "GET",
                QueryId = "q1",
                RequestTime = now,
                DurationMs = 100,
                StatusCode = 200,
                IsSuccess = true
            }
        };
        _usageTrackingService
            .Setup(x => x.GetUsageByDateRangeAsync(user.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(usage);
        _planPolicyService.Setup(x => x.GetDailyUsageCountAsync(user.Id, null)).ReturnsAsync(0);
        _planPolicyService.Setup(x => x.GetDailyRequestLimitAsync(user)).ReturnsAsync(5);

        var result = await _service.GetUsageAsync(7);

        var record = result.Usage!.UsageRecords.Single();
        record.Id.Should().Be(1);
        record.Endpoint.Should().Be("/api/test");
        record.HttpMethod.Should().Be("GET");
        record.QueryId.Should().Be("q1");
        record.RequestTime.Should().Be(now);
        record.DurationMs.Should().Be(100);
        record.StatusCode.Should().Be(200);
        record.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task GetUsageAsync_WhenDailyCountExceedsLimit_RemainingIsZero()
    {
        var user = CreateUser();
        SetupAuthenticatedUser(user);
        _usageTrackingService
            .Setup(x => x.GetUsageByDateRangeAsync(user.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(Array.Empty<Usage>());
        _planPolicyService.Setup(x => x.GetDailyUsageCountAsync(user.Id, null)).ReturnsAsync(15);
        _planPolicyService.Setup(x => x.GetDailyRequestLimitAsync(user)).ReturnsAsync(10);

        var result = await _service.GetUsageAsync(7);

        result.Usage!.RemainingDailyRequests.Should().Be(0);
    }

    #endregion

    #region UpdateProfileAsync

    [Fact]
    public async Task UpdateProfileAsync_WhenRequestIsNull_ThrowsArgumentNullException()
    {
        var action = async () => await _service.UpdateProfileAsync(null!);

        await action.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task UpdateProfileAsync_WhenNotAuthenticated_ReturnsUnauthenticated()
    {
        _currentUserService.SetupGet(x => x.IsAuthenticated).Returns(false);

        var result = await _service.UpdateProfileAsync(new UpdateProfileRequest { DisplayName = "New" });

        result.Status.Should().Be(CurrentUserProfileStatus.Unauthenticated);
    }

    [Fact]
    public async Task UpdateProfileAsync_WhenAccessNotApproved_ReturnsAccessNotApproved()
    {
        _currentUserService.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUserService.Setup(x => x.GetManagedUserAsync()).ReturnsAsync((UserDTO?)null);

        var result = await _service.UpdateProfileAsync(new UpdateProfileRequest { DisplayName = "New" });

        result.Status.Should().Be(CurrentUserProfileStatus.AccessNotApproved);
    }

    [Fact]
    public async Task UpdateProfileAsync_WhenAllFieldsEmpty_ReturnsValidationFailed()
    {
        var user = CreateUser();
        SetupAuthenticatedUser(user);

        var result = await _service.UpdateProfileAsync(new UpdateProfileRequest());

        result.Status.Should().Be(CurrentUserProfileStatus.ValidationFailed);
        result.ValidationErrors.Should().Contain("At least one field (DisplayName, FirstName, LastName) must be provided");
    }

    [Fact]
    public async Task UpdateProfileAsync_WhenDisplayNameTooLong_ReturnsValidationFailed()
    {
        var user = CreateUser();
        SetupAuthenticatedUser(user);

        var result = await _service.UpdateProfileAsync(new UpdateProfileRequest { DisplayName = new string('x', 101) });

        result.Status.Should().Be(CurrentUserProfileStatus.ValidationFailed);
        result.ValidationErrors.Should().Contain("DisplayName cannot exceed 100 characters");
    }

    [Fact]
    public async Task UpdateProfileAsync_WhenFirstNameTooLong_ReturnsValidationFailed()
    {
        var user = CreateUser();
        SetupAuthenticatedUser(user);

        var result = await _service.UpdateProfileAsync(new UpdateProfileRequest { FirstName = new string('x', 51) });

        result.Status.Should().Be(CurrentUserProfileStatus.ValidationFailed);
        result.ValidationErrors.Should().Contain("FirstName cannot exceed 50 characters");
    }

    [Fact]
    public async Task UpdateProfileAsync_WhenLastNameTooLong_ReturnsValidationFailed()
    {
        var user = CreateUser();
        SetupAuthenticatedUser(user);

        var result = await _service.UpdateProfileAsync(new UpdateProfileRequest { LastName = new string('x', 51) });

        result.Status.Should().Be(CurrentUserProfileStatus.ValidationFailed);
        result.ValidationErrors.Should().Contain("LastName cannot exceed 50 characters");
    }

    [Fact]
    public async Task UpdateProfileAsync_WhenMultipleValidationErrors_ReturnsAllErrors()
    {
        var user = CreateUser();
        SetupAuthenticatedUser(user);

        var result = await _service.UpdateProfileAsync(new UpdateProfileRequest
        {
            DisplayName = new string('x', 101),
            FirstName = new string('x', 51),
            LastName = new string('x', 51)
        });

        result.Status.Should().Be(CurrentUserProfileStatus.ValidationFailed);
        result.ValidationErrors.Should().HaveCount(3);
    }

    [Fact]
    public async Task UpdateProfileAsync_WhenNoChangesNeeded_DoesNotCallUpdateUser()
    {
        var user = CreateUser(displayName: "Same", firstName: "Same", lastName: "Same");
        SetupAuthenticatedUser(user);
        _planPolicyService.Setup(x => x.GetDailyRequestLimitAsync(user)).ReturnsAsync(10);
        _planPolicyService.Setup(x => x.GetRemainingDailyRequestsAsync(user.Id, null)).ReturnsAsync(10);

        var result = await _service.UpdateProfileAsync(new UpdateProfileRequest { DisplayName = "Same", FirstName = "Same", LastName = "Same" });

        result.Status.Should().Be(CurrentUserProfileStatus.Success);
        _userRepository.Verify(x => x.UpdateUserAsync(It.IsAny<UserDTO>()), Times.Never);
    }

    [Fact]
    public async Task UpdateProfileAsync_WhenDisplayNameChanged_UpdatesUser()
    {
        var user = CreateUser(displayName: "Old");
        SetupAuthenticatedUser(user);
        _userRepository.Setup(x => x.UpdateUserAsync(It.IsAny<UserDTO>())).ReturnsAsync(true);
        _planPolicyService.Setup(x => x.GetDailyRequestLimitAsync(user)).ReturnsAsync(10);
        _planPolicyService.Setup(x => x.GetRemainingDailyRequestsAsync(user.Id, null)).ReturnsAsync(10);

        var result = await _service.UpdateProfileAsync(new UpdateProfileRequest { DisplayName = "New" });

        result.Status.Should().Be(CurrentUserProfileStatus.Success);
        result.Profile!.DisplayName.Should().Be("New");
        _userRepository.Verify(
            x => x.UpdateUserAsync(It.Is<UserDTO>(u =>
                u.DisplayName == "New" &&
                u.LastUpdatedDate.HasValue &&
                (DateTime.UtcNow - u.LastUpdatedDate.Value).TotalSeconds < 5)),
            Times.Once);
    }

    [Fact]
    public async Task UpdateProfileAsync_WhenFirstNameChanged_UpdatesUser()
    {
        var user = CreateUser(firstName: "Old");
        SetupAuthenticatedUser(user);
        _userRepository.Setup(x => x.UpdateUserAsync(It.IsAny<UserDTO>())).ReturnsAsync(true);
        _planPolicyService.Setup(x => x.GetDailyRequestLimitAsync(user)).ReturnsAsync(10);
        _planPolicyService.Setup(x => x.GetRemainingDailyRequestsAsync(user.Id, null)).ReturnsAsync(10);

        var result = await _service.UpdateProfileAsync(new UpdateProfileRequest { FirstName = "New" });

        result.Status.Should().Be(CurrentUserProfileStatus.Success);
        result.Profile!.FirstName.Should().Be("New");
        _userRepository.Verify(
            x => x.UpdateUserAsync(It.Is<UserDTO>(u => u.FirstName == "New")),
            Times.Once);
    }

    [Fact]
    public async Task UpdateProfileAsync_WhenLastNameChanged_UpdatesUser()
    {
        var user = CreateUser(lastName: "Old");
        SetupAuthenticatedUser(user);
        _userRepository.Setup(x => x.UpdateUserAsync(It.IsAny<UserDTO>())).ReturnsAsync(true);
        _planPolicyService.Setup(x => x.GetDailyRequestLimitAsync(user)).ReturnsAsync(10);
        _planPolicyService.Setup(x => x.GetRemainingDailyRequestsAsync(user.Id, null)).ReturnsAsync(10);

        var result = await _service.UpdateProfileAsync(new UpdateProfileRequest { LastName = "New" });

        result.Status.Should().Be(CurrentUserProfileStatus.Success);
        result.Profile!.LastName.Should().Be("New");
        _userRepository.Verify(
            x => x.UpdateUserAsync(It.Is<UserDTO>(u => u.LastName == "New")),
            Times.Once);
    }

    [Fact]
    public async Task UpdateProfileAsync_WhenMultipleFieldsChanged_UpdatesAll()
    {
        var user = CreateUser(displayName: "Old", firstName: "Old", lastName: "Old");
        SetupAuthenticatedUser(user);
        _userRepository.Setup(x => x.UpdateUserAsync(It.IsAny<UserDTO>())).ReturnsAsync(true);
        _planPolicyService.Setup(x => x.GetDailyRequestLimitAsync(user)).ReturnsAsync(10);
        _planPolicyService.Setup(x => x.GetRemainingDailyRequestsAsync(user.Id, null)).ReturnsAsync(10);

        var result = await _service.UpdateProfileAsync(new UpdateProfileRequest { DisplayName = "NewD", FirstName = "NewF", LastName = "NewL" });

        result.Status.Should().Be(CurrentUserProfileStatus.Success);
        _userRepository.Verify(
            x => x.UpdateUserAsync(It.Is<UserDTO>(u =>
                u.DisplayName == "NewD" &&
                u.FirstName == "NewF" &&
                u.LastName == "NewL")),
            Times.Once);
    }

    #endregion

    private UserDTO CreateUser(string displayName = "Display", string firstName = "First", string lastName = "Last")
    {
        return new UserDTO
        {
            Id = "user-id",
            Email = "user@example.com",
            DisplayName = displayName,
            FirstName = firstName,
            LastName = lastName,
            IsEnabled = true,
            CreatedDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedDate = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            PlanId = "plan-1"
        };
    }

    private void SetupAuthenticatedUser(UserDTO user)
    {
        _currentUserService.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUserService.Setup(x => x.GetManagedUserAsync()).ReturnsAsync(user);
    }
}
