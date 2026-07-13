using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Domain.Entities;
using Xunit;

namespace MotorcycleRAG.UnitTests.Services;

public class AccessRequestAdminServiceTests
{
    private readonly Mock<IAccessRequestRepository> _accessRepoMock;
    private readonly Mock<IUserManagementQueryRepository> _queryRepoMock;
    private readonly Mock<ApprovalOnboardingService> _onboardingServiceMock;
    private readonly Mock<UserAccessLifecycleService> _lifecycleServiceMock;
    private readonly Mock<ITelemetryService> _telemetryMock;
    private readonly AccessRequestAdminService _sut;

    public AccessRequestAdminServiceTests()
    {
        _accessRepoMock = new Mock<IAccessRequestRepository>();
        _queryRepoMock = new Mock<IUserManagementQueryRepository>();
        
        var externalIdProvMock = new Mock<IExternalIdentityProvisioningService>();
        var userRepoMock = new Mock<IUserRepository>();
        var userIdentityRepoMock = new Mock<IUserIdentityRepository>();
        var planRepoMock = new Mock<IPlanRepository>();
        var usageTrackingMock = new Mock<IUsageTrackingService>();
        var tierEntitlementMock = new Mock<TierEntitlementMappingService>(NullLogger<TierEntitlementMappingService>.Instance);
        
        _onboardingServiceMock = new Mock<ApprovalOnboardingService>(
            _accessRepoMock.Object, 
            userRepoMock.Object, 
            userIdentityRepoMock.Object, 
            planRepoMock.Object, 
            usageTrackingMock.Object, 
            externalIdProvMock.Object, 
            tierEntitlementMock.Object, 
            new Mock<ITelemetryService>().Object, 
            NullLogger<ApprovalOnboardingService>.Instance);

        _lifecycleServiceMock = new Mock<UserAccessLifecycleService>(
            userRepoMock.Object,
            userIdentityRepoMock.Object,
            planRepoMock.Object,
            _queryRepoMock.Object,
            externalIdProvMock.Object,
            tierEntitlementMock.Object,
            new Mock<ITelemetryService>().Object,
            NullLogger<UserAccessLifecycleService>.Instance);

        _telemetryMock = new Mock<ITelemetryService>();

        _sut = new AccessRequestAdminService(
            _accessRepoMock.Object,
            _queryRepoMock.Object,
            _onboardingServiceMock.Object,
            _lifecycleServiceMock.Object,
            _telemetryMock.Object,
            NullLogger<AccessRequestAdminService>.Instance
        );
    }

    [Fact]
    public void Constructor_NullDependencies_ThrowsArgumentNullException()
    {
        var logger = NullLogger<AccessRequestAdminService>.Instance;
        Assert.Throws<ArgumentNullException>(() => new AccessRequestAdminService(null!, _queryRepoMock.Object, _onboardingServiceMock.Object, _lifecycleServiceMock.Object, _telemetryMock.Object, logger));
        Assert.Throws<ArgumentNullException>(() => new AccessRequestAdminService(_accessRepoMock.Object, null!, _onboardingServiceMock.Object, _lifecycleServiceMock.Object, _telemetryMock.Object, logger));
    }

    [Fact]
    public async Task GetUserManagementRowsAsync_ValidPaging_ReturnsRows()
    {
        var response = new UserManagementListResponse { Rows = new UserManagementRow[1] };
        _queryRepoMock.Setup(x => x.GetRowsAsync(It.IsAny<UserManagementRowState?>(), It.IsAny<string>(), 1, 10))
            .ReturnsAsync(response);

        var result = await _sut.GetUserManagementRowsAsync(null, null, 1, 10);

        result.Should().Be(response);
    }

    [Fact]
    public async Task GetUserManagementRowsAsync_InvalidPaging_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.GetUserManagementRowsAsync(null, null, 0, 10));
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.GetUserManagementRowsAsync(null, null, 1, 0));
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.GetUserManagementRowsAsync(null, null, 1, 101));
    }

    [Fact]
    public async Task ApproveAccessRequestAsync_PendingRequest_ApprovesAndExecutesOnboarding()
    {
        var requestRecord = new AccessRequestAdminRecord { RequestId = "req-1", RequestDecisionState = RequestDecisionState.Pending };
        var inProgressRecord = new AccessRequestAdminRecord { RequestId = "req-1", RequestDecisionState = RequestDecisionState.Approved };
        var requestDto = new ApproveAccessRequestRequest { Tier = TierLabel.RoadRunner, ExpectedRowVersion = "v1" };
        var row = new UserManagementRow();
        
        _accessRepoMock.Setup(x => x.GetAdminRecordByRequestIdAsync("req-1")).ReturnsAsync(requestRecord);
        _accessRepoMock.Setup(x => x.BeginApprovalOnboardingAsync("req-1", TierLabel.RoadRunner, "v1", "admin"))
            .ReturnsAsync(inProgressRecord);
        _queryRepoMock.Setup(x => x.GetRowByIdAsync("request:req-1")).ReturnsAsync(row);
        _onboardingServiceMock.Setup(x => x.ExecuteAsync(inProgressRecord)).Returns(Task.CompletedTask);

        var result = await _sut.ApproveAccessRequestAsync("req-1", requestDto, "admin");

        result.Row.Should().Be(row);
        _onboardingServiceMock.Verify(x => x.ExecuteAsync(inProgressRecord), Times.Once);
        _telemetryMock.Verify(x => x.TrackAdminAction("ApproveAccessRequest", "req-1", "admin", true, It.IsAny<TimeSpan>()), Times.Once);
    }

    [Fact]
    public async Task ApproveAccessRequestAsync_NotPending_ThrowsInvalidOperationException()
    {
        var requestRecord = new AccessRequestAdminRecord { RequestId = "req-1", RequestDecisionState = RequestDecisionState.Approved };
        var requestDto = new ApproveAccessRequestRequest { Tier = TierLabel.RoadRunner, ExpectedRowVersion = "v1" };
        
        _accessRepoMock.Setup(x => x.GetAdminRecordByRequestIdAsync("req-1")).ReturnsAsync(requestRecord);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.ApproveAccessRequestAsync("req-1", requestDto, "admin"));
    }

    [Fact]
    public async Task RetryOnboardingAsync_FailedOnboarding_Retries()
    {
        var requestRecord = new AccessRequestAdminRecord { RequestId = "req-1", RequestDecisionState = RequestDecisionState.Approved, OnboardingExecutionState = OnboardingExecutionState.Failed };
        var inProgressRecord = new AccessRequestAdminRecord { RequestId = "req-1", RequestDecisionState = RequestDecisionState.Approved };
        var requestDto = new RetryAccessRequestOnboardingRequest { ExpectedRowVersion = "v1" };
        var row = new UserManagementRow();
        
        _accessRepoMock.Setup(x => x.GetAdminRecordByRequestIdAsync("req-1")).ReturnsAsync(requestRecord);
        _accessRepoMock.Setup(x => x.RetryOnboardingAsync("req-1", "v1"))
            .ReturnsAsync(inProgressRecord);
        _queryRepoMock.Setup(x => x.GetRowByIdAsync("request:req-1")).ReturnsAsync(row);
        _onboardingServiceMock.Setup(x => x.ExecuteAsync(inProgressRecord)).Returns(Task.CompletedTask);

        var result = await _sut.RetryOnboardingAsync("req-1", requestDto);

        result.Row.Should().Be(row);
        _onboardingServiceMock.Verify(x => x.ExecuteAsync(inProgressRecord), Times.Once);
    }

    [Fact]
    public async Task CancelAccessRequestAsync_Pending_CancelsRequest()
    {
        var requestRecord = new AccessRequestAdminRecord { RequestId = "req-1", RequestDecisionState = RequestDecisionState.Pending, ManagedUserId = "user-1" };
        var cancelledRecord = new AccessRequestAdminRecord { RequestId = "req-1", ManagedUserId = "user-1" };
        var requestDto = new CancelAccessRequestRequest { ExpectedRowVersion = "v1", Reason = "Duplicate" };
        var requestRow = new UserManagementRow();
        var userRow = new UserManagementRow { RowVersion = "user-v1", ManagedUserAccessState = ManagedUserAccessState.Active };
        
        _accessRepoMock.Setup(x => x.GetAdminRecordByRequestIdAsync("req-1")).ReturnsAsync(requestRecord);
        _accessRepoMock.Setup(x => x.CancelAsync("req-1", "v1", "Duplicate", "admin")).ReturnsAsync(cancelledRecord);
        _queryRepoMock.Setup(x => x.GetRowByIdAsync("user:user-1")).ReturnsAsync(userRow);
        _queryRepoMock.Setup(x => x.GetRowByIdAsync("request:req-1")).ReturnsAsync(requestRow);
        
        _lifecycleServiceMock.Setup(x => x.CancelManagedUserAsync("user-1", It.IsAny<CancelManagedUserRequest>(), "admin")).ReturnsAsync(new AdminActionResponse { Row = requestRow });

        var result = await _sut.CancelAccessRequestAsync("req-1", requestDto, "admin");

        result.Row.Should().Be(requestRow);
        _lifecycleServiceMock.Verify(x => x.CancelManagedUserAsync("user-1", It.Is<CancelManagedUserRequest>(r => r.Reason == "Duplicate" && r.ExpectedRowVersion == "user-v1"), "admin"), Times.Once);
    }
}
