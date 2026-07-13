using System.Collections;
using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Persistence.Sql;
using MotorcycleRAG.Persistence.Sql.Repositories;

namespace MotorcycleRAG.UnitTests.Persistence.Sql.Repositories;

public sealed class AccessRequestRepositoryTests
{
    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenConnectionFactoryIsNull()
    {
        var act = () => new AccessRequestRepository(null!, NullLogger<AccessRequestRepository>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        var factory = new Mock<ISqlConnectionFactory>();

        var act = () => new AccessRequestRepository(factory.Object, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task GetByProviderAndEmailAsync_ShouldThrowArgumentException_WhenEmailIsBlank(string? email)
    {
        var sut = CreateSut();

        var act = async () => await sut.GetByProviderAndEmailAsync(email!, IdentityProvider.Microsoft);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("email");
    }

    [Fact]
    public async Task GetByProviderAndEmailAsync_ShouldReturnNull_WhenNoRowExists()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(CreateReader());
        var sut = CreateSut(connection);

        var result = await sut.GetByProviderAndEmailAsync("rider@example.com", IdentityProvider.Google);

        result.Should().BeNull();
        connection.ExecutedCommands.Should().ContainSingle()
            .Which.Parameters["Email"].Should().Be("rider@example.com");
        connection.ExecutedCommands[0].Parameters["Provider"].Should().Be(IdentityProvider.Google.ToString());
    }

    [Theory]
    [InlineData(RequestDecisionState.Pending, OnboardingExecutionState.NotStarted, "PendingReview", "WaitForReview", UserManagementRowState.PendingApproval)]
    [InlineData(RequestDecisionState.Approved, OnboardingExecutionState.InProgress, "ProvisioningAccess", "WaitForProvisioning", UserManagementRowState.OnboardingInProgress)]
    [InlineData(RequestDecisionState.Approved, OnboardingExecutionState.Completed, "ApprovedReadyToSignIn", "SignInWithApprovedProvider", UserManagementRowState.Active)]
    [InlineData(RequestDecisionState.Approved, OnboardingExecutionState.Failed, "OnboardingDelayed", "WaitForAdminRetry", UserManagementRowState.OnboardingFailed)]
    [InlineData(RequestDecisionState.Cancelled, OnboardingExecutionState.NotRequired, "Cancelled", "SubmitNewRequest", UserManagementRowState.Cancelled)]
    public async Task GetByProviderAndEmailAsync_ShouldMapPublicResponse_ForAllVisibleStates(
        RequestDecisionState decision,
        OnboardingExecutionState onboarding,
        string visibleStatus,
        string nextAction,
        UserManagementRowState rowState)
    {
        var requestId = Guid.NewGuid().ToString("D");
        var requestedAt = new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);
        var connection = new FakeDbConnection();
        connection.EnqueueReader(CreateReader(CreatePublicRow(
            requestId,
            decision,
            onboarding,
            requestedAt)));
        var sut = CreateSut(connection);

        var result = await sut.GetByProviderAndEmailAsync("rider@example.com", IdentityProvider.Microsoft);

        result.Should().NotBeNull();
        result!.RequestId.Should().Be(requestId);
        result.Email.Should().Be("rider@example.com");
        result.Provider.Should().Be(IdentityProvider.Microsoft);
        result.RequestDecisionState.Should().Be(decision);
        result.OnboardingExecutionState.Should().Be(onboarding);
        result.RequesterVisibleStatus.Should().Be(visibleStatus);
        result.NextAction.Should().Be(nextAction);
        result.RowState.Should().Be(rowState);
        result.StatusMessage.Should().NotBeNullOrWhiteSpace();
        result.RequestedAtUtc.Should().Be(requestedAt);
        result.CorrelationId.Should().Be("corr-1");
        result.RowVersion.Should().Be("0x01");
    }

    [Fact]
    public async Task GetByProviderAndEmailAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetByProviderAndEmailAsync("rider@example.com", IdentityProvider.Microsoft);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Failed to get access request for rider@example.com");
        exception.Which.InnerException.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task GetByRequestIdAsync_ShouldThrowArgumentException_WhenRequestIdIsBlank(string? requestId)
    {
        var sut = CreateSut();

        var act = async () => await sut.GetByRequestIdAsync(requestId!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("requestId");
    }

    [Fact]
    public async Task GetByRequestIdAsync_ShouldThrowArgumentException_WhenRequestIdIsNotGuid()
    {
        var sut = CreateSut();

        var act = async () => await sut.GetByRequestIdAsync("not-a-guid");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("requestId");
    }

    [Fact]
    public async Task GetByRequestIdAsync_ShouldReturnMappedRow()
    {
        var requestId = Guid.NewGuid();
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            CreateReader(CreatePublicRow(requestId.ToString("D"))),
            command => command.Parameters["AccessRequestId"].Should().Be(requestId));
        var sut = CreateSut(connection);

        var result = await sut.GetByRequestIdAsync(requestId.ToString("D"));

        result.Should().NotBeNull();
        result!.RequestId.Should().Be(requestId.ToString("D"));
        result.RequesterVisibleStatus.Should().Be("PendingReview");
    }

    [Fact]
    public async Task GetByRequestIdAsync_ShouldWrapConnectionFailures()
    {
        var requestId = Guid.NewGuid().ToString("D");
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetByRequestIdAsync(requestId);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain($"Failed to get access request {requestId}");
        exception.Which.InnerException.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task GetAdminRecordByRequestIdAsync_ShouldThrowArgumentException_WhenRequestIdIsBlank(string? requestId)
    {
        var sut = CreateSut();

        var act = async () => await sut.GetAdminRecordByRequestIdAsync(requestId!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("requestId");
    }

    [Fact]
    public async Task GetAdminRecordByRequestIdAsync_ShouldThrowArgumentException_WhenRequestIdIsNotGuid()
    {
        var sut = CreateSut();

        var act = async () => await sut.GetAdminRecordByRequestIdAsync("bad-id");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("requestId");
    }

    [Fact]
    public async Task GetAdminRecordByRequestIdAsync_ShouldReturnNull_WhenNoRowExists()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(CreateReader());
        var sut = CreateSut(connection);

        var result = await sut.GetAdminRecordByRequestIdAsync(Guid.NewGuid().ToString("D"));

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAdminRecordByRequestIdAsync_ShouldMapAssignedTier_WhenPresent()
    {
        var requestId = Guid.NewGuid();
        var connection = new FakeDbConnection();
        connection.EnqueueReader(CreateReader(CreateAdminRow(
            requestId.ToString("D"),
            assignedTier: TierLabel.RoadRunner.ToString())));
        var sut = CreateSut(connection);

        var result = await sut.GetAdminRecordByRequestIdAsync(requestId.ToString("D"));

        result.Should().NotBeNull();
        result!.AssignedTier.Should().Be(TierLabel.RoadRunner);
        result.ManagedUserId.Should().Be("managed-1");
        result.ExternalDirectoryObjectId.Should().Be("ext-1");
        result.OnboardingAttemptCount.Should().Be(2);
        result.LastFailureCode.Should().BeNull();
    }

    [Fact]
    public async Task GetAdminRecordByRequestIdAsync_ShouldLeaveAssignedTierNull_WhenBlank()
    {
        var requestId = Guid.NewGuid();
        var connection = new FakeDbConnection();
        connection.EnqueueReader(CreateReader(CreateAdminRow(
            requestId.ToString("D"),
            assignedTier: " ")));
        var sut = CreateSut(connection);

        var result = await sut.GetAdminRecordByRequestIdAsync(requestId.ToString("D"));

        result.Should().NotBeNull();
        result!.AssignedTier.Should().BeNull();
    }

    [Fact]
    public async Task GetAdminRecordByRequestIdAsync_ShouldWrapConnectionFailures()
    {
        var requestId = Guid.NewGuid().ToString("D");
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetAdminRecordByRequestIdAsync(requestId);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain($"Failed to get access request {requestId}");
        exception.Which.InnerException.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task BeginApprovalOnboardingAsync_ShouldThrowArgumentException_WhenExpectedRowVersionIsBlank(string? rowVersion)
    {
        var sut = CreateSut();

        var act = async () => await sut.BeginApprovalOnboardingAsync(
            Guid.NewGuid().ToString("D"),
            TierLabel.Trial,
            rowVersion!,
            "admin-1");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("expectedRowVersion");
    }

    [Fact]
    public async Task BeginApprovalOnboardingAsync_ShouldReturnUpdatedAdminRecord()
    {
        var requestId = Guid.NewGuid();
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            CreateReader(CreateAdminRow(
                requestId.ToString("D"),
                decision: RequestDecisionState.Approved,
                onboarding: OnboardingExecutionState.InProgress,
                assignedTier: TierLabel.Trial.ToString())),
            command =>
            {
                command.CommandText.Should().Contain("RequestDecisionState] = N'Approved'");
                command.Parameters["AccessRequestId"].Should().Be(requestId);
                command.Parameters["ExpectedRowVersion"].Should().Be("0xAA");
                command.Parameters["AssignedTier"].Should().Be(TierLabel.Trial.ToString());
                command.Parameters["ApprovedByUserId"].Should().Be("admin-1");
            });
        var sut = CreateSut(connection);

        var result = await sut.BeginApprovalOnboardingAsync(
            requestId.ToString("D"),
            TierLabel.Trial,
            "0xAA",
            "admin-1");

        result.Should().NotBeNull();
        result!.RequestDecisionState.Should().Be(RequestDecisionState.Approved);
        result.OnboardingExecutionState.Should().Be(OnboardingExecutionState.InProgress);
        result.AssignedTier.Should().Be(TierLabel.Trial);
    }

    [Fact]
    public async Task RetryOnboardingAsync_ShouldReturnNull_WhenNoRowUpdated()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(CreateReader());
        var sut = CreateSut(connection);

        var result = await sut.RetryOnboardingAsync(Guid.NewGuid().ToString("D"), "0xBB");

        result.Should().BeNull();
    }

    [Fact]
    public async Task RetryOnboardingAsync_ShouldReturnUpdatedAdminRecord()
    {
        var requestId = Guid.NewGuid();
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            CreateReader(CreateAdminRow(
                requestId.ToString("D"),
                decision: RequestDecisionState.Approved,
                onboarding: OnboardingExecutionState.InProgress)),
            command =>
            {
                command.CommandText.Should().Contain("OnboardingExecutionState] = N'Failed'");
                command.Parameters["ExpectedRowVersion"].Should().Be("0xBB");
            });
        var sut = CreateSut(connection);

        var result = await sut.RetryOnboardingAsync(requestId.ToString("D"), "0xBB");

        result.Should().NotBeNull();
        result!.OnboardingExecutionState.Should().Be(OnboardingExecutionState.InProgress);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task CompleteOnboardingAsync_ShouldThrowArgumentException_WhenManagedUserIdIsBlank(string? managedUserId)
    {
        var sut = CreateSut();

        var act = async () => await sut.CompleteOnboardingAsync(
            Guid.NewGuid().ToString("D"),
            managedUserId!,
            "ext-1");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("managedUserId");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task CompleteOnboardingAsync_ShouldThrowArgumentException_WhenExternalDirectoryObjectIdIsBlank(string? externalId)
    {
        var sut = CreateSut();

        var act = async () => await sut.CompleteOnboardingAsync(
            Guid.NewGuid().ToString("D"),
            "managed-1",
            externalId!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("externalDirectoryObjectId");
    }

    [Fact]
    public async Task CompleteOnboardingAsync_ShouldReturnCompletedAdminRecord()
    {
        var requestId = Guid.NewGuid().ToString("D");
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            CreateReader(CreateAdminRow(
                requestId,
                decision: RequestDecisionState.Approved,
                onboarding: OnboardingExecutionState.Completed,
                assignedTier: TierLabel.Admin.ToString(),
                managedUserId: "managed-9",
                externalDirectoryObjectId: "ext-9")),
            command =>
            {
                command.CommandText.Should().Contain("OnboardingExecutionState] = N'Completed'");
                command.Parameters["ManagedUserId"].Should().Be("managed-9");
                command.Parameters["ExternalDirectoryObjectId"].Should().Be("ext-9");
            });
        var sut = CreateSut(connection);

        var result = await sut.CompleteOnboardingAsync(requestId, "managed-9", "ext-9");

        result.Should().NotBeNull();
        result!.OnboardingExecutionState.Should().Be(OnboardingExecutionState.Completed);
        result.ManagedUserId.Should().Be("managed-9");
        result.ExternalDirectoryObjectId.Should().Be("ext-9");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task FailOnboardingAsync_ShouldThrowArgumentException_WhenFailureCodeIsBlank(string? failureCode)
    {
        var sut = CreateSut();

        var act = async () => await sut.FailOnboardingAsync(
            Guid.NewGuid().ToString("D"),
            failureCode!,
            "message",
            null,
            null);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("failureCode");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task FailOnboardingAsync_ShouldThrowArgumentException_WhenFailureMessageIsBlank(string? failureMessage)
    {
        var sut = CreateSut();

        var act = async () => await sut.FailOnboardingAsync(
            Guid.NewGuid().ToString("D"),
            "GRAPH_ERROR",
            failureMessage!,
            null,
            null);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("failureMessage");
    }

    [Fact]
    public async Task FailOnboardingAsync_ShouldReturnFailedAdminRecord()
    {
        var requestId = Guid.NewGuid().ToString("D");
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            CreateReader(CreateAdminRow(
                requestId,
                decision: RequestDecisionState.Approved,
                onboarding: OnboardingExecutionState.Failed,
                lastFailureCode: "GRAPH_ERROR",
                lastFailureMessage: "invite failed")),
            command =>
            {
                command.CommandText.Should().Contain("OnboardingExecutionState] = N'Failed'");
                command.Parameters["FailureCode"].Should().Be("GRAPH_ERROR");
                command.Parameters["FailureMessage"].Should().Be("invite failed");
                command.Parameters["ManagedUserId"].Should().Be("managed-partial");
                command.Parameters["ExternalDirectoryObjectId"].Should().Be("ext-partial");
            });
        var sut = CreateSut(connection);

        var result = await sut.FailOnboardingAsync(
            requestId,
            "GRAPH_ERROR",
            "invite failed",
            "managed-partial",
            "ext-partial");

        result.Should().NotBeNull();
        result!.OnboardingExecutionState.Should().Be(OnboardingExecutionState.Failed);
        result.LastFailureCode.Should().Be("GRAPH_ERROR");
        result.LastFailureMessage.Should().Be("invite failed");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task CancelAsync_ShouldThrowArgumentException_WhenReasonIsBlank(string? reason)
    {
        var sut = CreateSut();

        var act = async () => await sut.CancelAsync(
            Guid.NewGuid().ToString("D"),
            "0xCC",
            reason!,
            "admin-2");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("reason");
    }

    [Fact]
    public async Task CancelAsync_ShouldReturnCancelledAdminRecord()
    {
        var requestId = Guid.NewGuid();
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            CreateReader(CreateAdminRow(
                requestId.ToString("D"),
                decision: RequestDecisionState.Cancelled,
                onboarding: OnboardingExecutionState.NotRequired)),
            command =>
            {
                command.CommandText.Should().Contain("RequestDecisionState] = N'Cancelled'");
                command.Parameters["CancelReason"].Should().Be("duplicate request");
                command.Parameters["CancelledByUserId"].Should().Be("admin-2");
                command.Parameters["ExpectedRowVersion"].Should().Be("0xCC");
            });
        var sut = CreateSut(connection);

        var result = await sut.CancelAsync(
            requestId.ToString("D"),
            "0xCC",
            "duplicate request",
            "admin-2");

        result.Should().NotBeNull();
        result!.RequestDecisionState.Should().Be(RequestDecisionState.Cancelled);
        result.OnboardingExecutionState.Should().Be(OnboardingExecutionState.NotRequired);
    }

    [Fact]
    public async Task CancelAsync_ShouldWrapUpdateFailures()
    {
        var requestId = Guid.NewGuid().ToString("D");
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.CancelAsync(requestId, "0xCC", "reason", null);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain($"Failed to update access request {requestId}");
        exception.Which.InnerException.Should().Be(expected);
    }

    [Fact]
    public async Task CreateAsync_ShouldThrowArgumentNullException_WhenRequestIsNull()
    {
        var sut = CreateSut();

        var act = async () => await sut.CreateAsync(null!, "corr-1");

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("request");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task CreateAsync_ShouldThrowArgumentException_WhenCorrelationIdIsBlank(string? correlationId)
    {
        var sut = CreateSut();
        var request = new CreateAccessRequestRequest
        {
            Email = "rider@example.com",
            Provider = IdentityProvider.Microsoft
        };

        var act = async () => await sut.CreateAsync(request, correlationId!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("correlationId");
    }

    [Fact]
    public async Task CreateAsync_ShouldReturnCreatedPublicResponse()
    {
        var requestId = Guid.NewGuid().ToString("D");
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            CreateReader(CreatePublicRow(requestId)),
            command =>
            {
                command.CommandText.Should().Contain("INSERT INTO [dbo].[AccessRequests]");
                command.Parameters["Email"].Should().Be("rider@example.com");
                command.Parameters["Provider"].Should().Be(IdentityProvider.Microsoft.ToString());
                command.Parameters["CorrelationId"].Should().Be("corr-create");
            });
        var sut = CreateSut(connection);

        var result = await sut.CreateAsync(
            new CreateAccessRequestRequest
            {
                Email = "rider@example.com",
                Provider = IdentityProvider.Microsoft
            },
            "corr-create");

        result.RequestId.Should().Be(requestId);
        result.Email.Should().Be("rider@example.com");
        result.Provider.Should().Be(IdentityProvider.Microsoft);
        result.RequestDecisionState.Should().Be(RequestDecisionState.Pending);
    }

    [Fact]
    public async Task CreateAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.CreateAsync(
            new CreateAccessRequestRequest
            {
                Email = "rider@example.com",
                Provider = IdentityProvider.Microsoft
            },
            "corr-1");

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Failed to create access request for rider@example.com");
        exception.Which.InnerException.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task ExistsPendingAsync_ShouldThrowArgumentException_WhenEmailIsBlank(string? email)
    {
        var sut = CreateSut();

        var act = async () => await sut.ExistsPendingAsync(email!, IdentityProvider.Microsoft);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("email");
    }

    [Fact]
    public async Task ExistsPendingAsync_ShouldReturnTrue_WhenCountIsPositive()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueScalar(
            2,
            command =>
            {
                command.CommandText.Should().Contain("RequestDecisionState] = N'Pending'");
                command.Parameters["Email"].Should().Be("rider@example.com");
                command.Parameters["Provider"].Should().Be(IdentityProvider.Microsoft.ToString());
            });
        var sut = CreateSut(connection);

        var result = await sut.ExistsPendingAsync("rider@example.com", IdentityProvider.Microsoft);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsPendingAsync_ShouldReturnFalse_WhenCountIsZero()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueScalar(0);
        var sut = CreateSut(connection);

        var result = await sut.ExistsPendingAsync("rider@example.com", IdentityProvider.Google);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsPendingAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.ExistsPendingAsync("rider@example.com", IdentityProvider.Microsoft);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Failed to check access request for rider@example.com");
        exception.Which.InnerException.Should().Be(expected);
    }

    private static AccessRequestRepository CreateSut(FakeDbConnection? connection = null)
    {
        var factory = new Mock<ISqlConnectionFactory>();
        factory
            .Setup(x => x.CreateOpenConnectionAsync())
            .ReturnsAsync(connection ?? new FakeDbConnection());
        return new AccessRequestRepository(factory.Object, NullLogger<AccessRequestRepository>.Instance);
    }

    private static AccessRequestRepository CreateThrowingSut(Exception exception)
    {
        var factory = new Mock<ISqlConnectionFactory>();
        factory.Setup(x => x.CreateOpenConnectionAsync()).ThrowsAsync(exception);
        return new AccessRequestRepository(factory.Object, NullLogger<AccessRequestRepository>.Instance);
    }

    private static Dictionary<string, object?> CreatePublicRow(
        string requestId,
        RequestDecisionState decision = RequestDecisionState.Pending,
        OnboardingExecutionState onboarding = OnboardingExecutionState.NotStarted,
        DateTime? requestedAtUtc = null) =>
        new()
        {
            ["RequestId"] = requestId,
            ["Email"] = "rider@example.com",
            ["Provider"] = IdentityProvider.Microsoft.ToString(),
            ["RequestDecisionState"] = decision.ToString(),
            ["OnboardingExecutionState"] = onboarding.ToString(),
            ["RequestedAtUtc"] = requestedAtUtc ?? new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc),
            ["CorrelationId"] = "corr-1",
            ["RowVersion"] = "0x01"
        };

    private static Dictionary<string, object?> CreateAdminRow(
        string requestId,
        RequestDecisionState decision = RequestDecisionState.Pending,
        OnboardingExecutionState onboarding = OnboardingExecutionState.NotStarted,
        string? assignedTier = null,
        string? managedUserId = "managed-1",
        string? externalDirectoryObjectId = "ext-1",
        string? lastFailureCode = null,
        string? lastFailureMessage = null) =>
        new()
        {
            ["RequestId"] = requestId,
            ["Email"] = "rider@example.com",
            ["Provider"] = IdentityProvider.Microsoft.ToString(),
            ["RequestDecisionState"] = decision.ToString(),
            ["OnboardingExecutionState"] = onboarding.ToString(),
            ["AssignedTier"] = assignedTier,
            ["ManagedUserId"] = managedUserId,
            ["ExternalDirectoryObjectId"] = externalDirectoryObjectId,
            ["CorrelationId"] = "corr-1",
            ["RequestedAtUtc"] = new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc),
            ["ApprovedAtUtc"] = new DateTime(2026, 7, 10, 13, 0, 0, DateTimeKind.Utc),
            ["CancelledAtUtc"] = null,
            ["OnboardingAttemptCount"] = 2,
            ["LastFailureCode"] = lastFailureCode,
            ["LastFailureMessage"] = lastFailureMessage,
            ["RowVersion"] = "0x01"
        };

    private static DbDataReader CreateReader(params IReadOnlyDictionary<string, object?>[] rows)
    {
        var table = new DataTable();
        if (rows.Length == 0)
        {
            table.Columns.Add("RequestId", typeof(object));
            return table.CreateDataReader();
        }

        var columnNames = rows.SelectMany(static row => row.Keys).Distinct(StringComparer.Ordinal).ToList();
        foreach (var columnName in columnNames)
        {
            table.Columns.Add(columnName, typeof(object));
        }

        foreach (var row in rows)
        {
            var dataRow = table.NewRow();
            foreach (var columnName in columnNames)
            {
                dataRow[columnName] = row.TryGetValue(columnName, out var value) ? value ?? DBNull.Value : DBNull.Value;
            }

            table.Rows.Add(dataRow);
        }

        return table.CreateDataReader();
    }

    private sealed class FakeDbConnection : DbConnection
    {
        private readonly Queue<CommandPlan> _plans = new();

        public List<ExecutedCommand> ExecutedCommands { get; } = [];

        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "Fake";
        public override string DataSource => "Fake";
        public override string ServerVersion => "1.0";
        public override ConnectionState State => ConnectionState.Open;

        public void EnqueueScalar(object? result, Action<ExecutedCommand>? assert = null)
        {
            _plans.Enqueue(new CommandPlan(CommandKind.Scalar, () => result, assert));
        }

        public void EnqueueReader(DbDataReader reader, Action<ExecutedCommand>? assert = null)
        {
            _plans.Enqueue(new CommandPlan(CommandKind.Reader, () => reader, assert));
        }

        public override void ChangeDatabase(string databaseName) { }
        public override void Close() { }
        public override void Open() { }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            throw new NotSupportedException();
        }

        protected override DbCommand CreateDbCommand() => new FakeDbCommand(this);

        internal object? Execute(CommandKind kind, FakeDbCommand command)
        {
            _plans.Should().NotBeEmpty("every repository call in these tests should have a planned DB response");
            var plan = _plans.Dequeue();
            plan.Kind.Should().Be(kind);

            var executed = new ExecutedCommand(command.CommandText, command.GetParameters());
            ExecutedCommands.Add(executed);
            plan.Assert?.Invoke(executed);
            return plan.ResultFactory();
        }
    }

    private sealed class FakeDbCommand : DbCommand
    {
        private readonly FakeDbConnection _connection;
        private readonly FakeDbParameterCollection _parameters = new();

        public FakeDbCommand(FakeDbConnection connection)
        {
            _connection = connection;
        }

        public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection DbConnection
        {
            get => _connection;
            set => throw new NotSupportedException();
        }

        protected override DbParameterCollection DbParameterCollection => _parameters;
        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel() { }
        public override int ExecuteNonQuery() => (int)(_connection.Execute(CommandKind.NonQuery, this) ?? 0);
        public override object? ExecuteScalar() => _connection.Execute(CommandKind.Scalar, this);
        public override void Prepare() { }
        protected override DbParameter CreateDbParameter() => new FakeDbParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
            (DbDataReader)(_connection.Execute(CommandKind.Reader, this)
                ?? throw new InvalidOperationException("Reader result was null."));

        internal Dictionary<string, object?> GetParameters() =>
            _parameters
                .Cast<FakeDbParameter>()
                .ToDictionary(parameter => parameter.ParameterName, parameter => parameter.Value, StringComparer.Ordinal);
    }

    private sealed class FakeDbParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> _parameters = [];

        public override int Count => _parameters.Count;
        public override object SyncRoot => ((ICollection)_parameters).SyncRoot;
        public override int Add(object value)
        {
            _parameters.Add((DbParameter)value);
            return _parameters.Count - 1;
        }

        public override void AddRange(Array values)
        {
            foreach (var value in values)
            {
                Add(value!);
            }
        }

        public override void Clear() => _parameters.Clear();
        public override bool Contains(object value) => _parameters.Contains((DbParameter)value);
        public override bool Contains(string value) => _parameters.Any(parameter => parameter.ParameterName == value);
        public override void CopyTo(Array array, int index) => ((ICollection)_parameters).CopyTo(array, index);
        public override IEnumerator GetEnumerator() => _parameters.GetEnumerator();
        public override int IndexOf(object value) => _parameters.IndexOf((DbParameter)value);
        public override int IndexOf(string parameterName) => _parameters.FindIndex(parameter => parameter.ParameterName == parameterName);
        public override void Insert(int index, object value) => _parameters.Insert(index, (DbParameter)value);
        public override void Remove(object value) => _parameters.Remove((DbParameter)value);
        public override void RemoveAt(int index) => _parameters.RemoveAt(index);

        public override void RemoveAt(string parameterName)
        {
            var index = IndexOf(parameterName);
            if (index >= 0)
            {
                _parameters.RemoveAt(index);
            }
        }

        protected override DbParameter GetParameter(int index) => _parameters[index];
        protected override DbParameter GetParameter(string parameterName) => _parameters[IndexOf(parameterName)];
        protected override void SetParameter(int index, DbParameter value) => _parameters[index] = value;

        protected override void SetParameter(string parameterName, DbParameter value)
        {
            var index = IndexOf(parameterName);
            if (index >= 0)
            {
                _parameters[index] = value;
            }
            else
            {
                _parameters.Add(value);
            }
        }
    }

    private sealed class FakeDbParameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;
        public override bool IsNullable { get; set; }
        public override string ParameterName { get; set; } = string.Empty;
        public override string SourceColumn { get; set; } = string.Empty;
        public override object? Value { get; set; }
        public override bool SourceColumnNullMapping { get; set; }
        public override int Size { get; set; }
        public override void ResetDbType() { }
    }

    private sealed record CommandPlan(
        CommandKind Kind,
        Func<object?> ResultFactory,
        Action<ExecutedCommand>? Assert);

    private sealed record ExecutedCommand(
        string CommandText,
        IReadOnlyDictionary<string, object?> Parameters);

    private enum CommandKind
    {
        Scalar,
        NonQuery,
        Reader
    }
}
