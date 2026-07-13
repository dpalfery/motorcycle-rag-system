using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Persistence.Notifications;

namespace MotorcycleRAG.Persistence.Tests.Notifications;

public class ApproverNotificationServiceTests
{
    private static PublicAccessRequestResponse ValidRequest => new()
    {
        RequestId = "req-123",
        Email = "test@example.com",
        Provider = IdentityProvider.Google,
        RequesterVisibleStatus = "Pending",
        StatusMessage = "",
        NextAction = "",
        RequestDecisionState = RequestDecisionState.Pending,
        OnboardingExecutionState = OnboardingExecutionState.NotStarted,
        RowState = UserManagementRowState.Active,
        RequestedAtUtc = DateTime.UtcNow,
        CorrelationId = "corr-abc"
    };

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        // Act
        var act = () => new ApproverNotificationService(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public async Task SendAccessRequestSubmittedAsync_WhenAccessRequestIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var sut = new ApproverNotificationService(
            TestHelpers.CreateNullLogger<ApproverNotificationService>());

        // Act
        var act = async () => await sut.SendAccessRequestSubmittedAsync(null!, "approver@example.com");

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("accessRequest");
    }

    [Fact]
    public async Task SendAccessRequestSubmittedAsync_WhenApproverAddressIsNull_ThrowsArgumentException()
    {
        // Arrange
        var sut = new ApproverNotificationService(
            TestHelpers.CreateNullLogger<ApproverNotificationService>());

        // Act
        var act = async () => await sut.SendAccessRequestSubmittedAsync(ValidRequest, null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("approverAddress");
    }

    [Fact]
    public async Task SendAccessRequestSubmittedAsync_WhenApproverAddressIsEmpty_ThrowsArgumentException()
    {
        // Arrange
        var sut = new ApproverNotificationService(
            TestHelpers.CreateNullLogger<ApproverNotificationService>());

        // Act
        var act = async () => await sut.SendAccessRequestSubmittedAsync(ValidRequest, "");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("approverAddress");
    }

    [Fact]
    public async Task SendAccessRequestSubmittedAsync_WhenApproverAddressIsWhitespace_ThrowsArgumentException()
    {
        // Arrange
        var sut = new ApproverNotificationService(
            TestHelpers.CreateNullLogger<ApproverNotificationService>());

        // Act
        var act = async () => await sut.SendAccessRequestSubmittedAsync(ValidRequest, "   ");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("approverAddress");
    }

    [Fact]
    public async Task SendAccessRequestSubmittedAsync_WithValidInput_CompletesSuccessfully()
    {
        // Arrange
        var sut = new ApproverNotificationService(
            TestHelpers.CreateNullLogger<ApproverNotificationService>());

        // Act
        var act = async () => await sut.SendAccessRequestSubmittedAsync(ValidRequest, "approver@example.com");

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendAccessRequestSubmittedAsync_WithValidInput_CompletesSynchronously()
    {
        // Arrange
        var sut = new ApproverNotificationService(
            TestHelpers.CreateNullLogger<ApproverNotificationService>());

        // Act
        var task = sut.SendAccessRequestSubmittedAsync(ValidRequest, "approver-address");

        // Assert — should return Task.CompletedTask (synchronously completed)
        task.IsCompleted.Should().BeTrue();
        task.Status.Should().Be(TaskStatus.RanToCompletion);
    }
}
