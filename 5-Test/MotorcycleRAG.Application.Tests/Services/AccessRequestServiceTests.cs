using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.UnitTests.Services;

public class AccessRequestServiceTests {
    [Fact]
    public async Task CreateOrGetExistingAsync_WhenApproverMissing_TracksNotificationDegradation() {
        var accessRequestRepository = new Mock<IAccessRequestRepository>();
        var notificationService = new Mock<IApproverNotificationService>();
        var correlationService = new Mock<ICorrelationService>();
        var telemetryService = new Mock<ITelemetryService>();

        correlationService.Setup(service => service.GetOrGenerateCorrelationId()).Returns("corr-001");

        var created = new PublicAccessRequestResponse {
            RequestId = "request-1",
            Email = "rider@example.com",
            Provider = IdentityProvider.Microsoft,
            RequestDecisionState = RequestDecisionState.Pending,
            OnboardingExecutionState = OnboardingExecutionState.NotStarted,
            RowState = UserManagementRowState.PendingApproval,
            CorrelationId = "corr-001",
            RequestedAtUtc = DateTime.UtcNow
        };

        accessRequestRepository
            .Setup(repository => repository.GetByProviderAndEmailAsync("rider@example.com", IdentityProvider.Microsoft))
            .ReturnsAsync((PublicAccessRequestResponse?)null);
        accessRequestRepository
            .Setup(repository => repository.CreateAsync(It.IsAny<CreateAccessRequestRequest>(), "corr-001"))
            .ReturnsAsync(created);

        var service = new AccessRequestService(
            accessRequestRepository.Object,
            notificationService.Object,
            correlationService.Object,
            telemetryService.Object,
            Options.Create(new OnboardingOptions()),
            new Mock<ILogger<AccessRequestService>>().Object);

        var result = await service.CreateOrGetExistingAsync(new CreateAccessRequestRequest {
            Email = "rider@example.com",
            Provider = IdentityProvider.Microsoft
        });

        Assert.True(result.Created);
        notificationService.Verify(
            service => service.SendAccessRequestSubmittedAsync(It.IsAny<PublicAccessRequestResponse>(), It.IsAny<string>()),
            Times.Never);
        telemetryService.Verify(
            service => service.TrackDependencyDegradation("ApproverNotification", "corr-001", It.IsAny<string>()),
            Times.Once);
    }
}
