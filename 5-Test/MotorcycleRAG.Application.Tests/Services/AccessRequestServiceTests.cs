using FluentAssertions;
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

    [Fact]
    public void Constructor_WithNullArguments_ThrowsArgumentNullException() {
        var repo = new Mock<IAccessRequestRepository>();
        var notify = new Mock<IApproverNotificationService>();
        var corr = new Mock<ICorrelationService>();
        var telemetry = new Mock<ITelemetryService>();
        var opts = Options.Create(new OnboardingOptions());
        var logger = new Mock<ILogger<AccessRequestService>>();

        var act1 = () => new AccessRequestService(null!, notify.Object, corr.Object, telemetry.Object, opts, logger.Object);
        act1.Should().Throw<ArgumentNullException>().WithParameterName("accessRequestRepository");

        var act2 = () => new AccessRequestService(repo.Object, null!, corr.Object, telemetry.Object, opts, logger.Object);
        act2.Should().Throw<ArgumentNullException>().WithParameterName("approverNotificationService");

        var act3 = () => new AccessRequestService(repo.Object, notify.Object, null!, telemetry.Object, opts, logger.Object);
        act3.Should().Throw<ArgumentNullException>().WithParameterName("correlationService");

        var act4 = () => new AccessRequestService(repo.Object, notify.Object, corr.Object, null!, opts, logger.Object);
        act4.Should().Throw<ArgumentNullException>().WithParameterName("telemetryService");

        var act5 = () => new AccessRequestService(repo.Object, notify.Object, corr.Object, telemetry.Object, null!, logger.Object);
        act5.Should().Throw<ArgumentNullException>().WithParameterName("onboardingOptions");

        var act6 = () => new AccessRequestService(repo.Object, notify.Object, corr.Object, telemetry.Object, opts, null!);
        act6.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task CreateOrGetExistingAsync_WithNullRequest_ThrowsArgumentNullException() {
        var service = new AccessRequestService(
            new Mock<IAccessRequestRepository>().Object,
            new Mock<IApproverNotificationService>().Object,
            new Mock<ICorrelationService>().Object,
            new Mock<ITelemetryService>().Object,
            Options.Create(new OnboardingOptions()),
            new Mock<ILogger<AccessRequestService>>().Object);

        var act = () => service.CreateOrGetExistingAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("request");
    }

    [Fact]
    public async Task CreateOrGetExistingAsync_WhenCreateAsyncRacesAndRecordIsRetrieved_ReturnsExistingState() {
        var accessRequestRepository = new Mock<IAccessRequestRepository>();
        var notificationService = new Mock<IApproverNotificationService>();
        var correlationService = new Mock<ICorrelationService>();
        var telemetryService = new Mock<ITelemetryService>();

        correlationService.Setup(service => service.GetOrGenerateCorrelationId()).Returns("corr-raced");

        accessRequestRepository
            .Setup(repository => repository.GetByProviderAndEmailAsync("rider@example.com", IdentityProvider.Microsoft))
            .ReturnsAsync((PublicAccessRequestResponse?)null);

        accessRequestRepository
            .Setup(repository => repository.CreateAsync(It.IsAny<CreateAccessRequestRequest>(), "corr-raced"))
            .ThrowsAsync(new InvalidOperationException("Duplicate request"));

        var existing = new PublicAccessRequestResponse {
            RequestId = "request-raced",
            Email = "rider@example.com",
            Provider = IdentityProvider.Microsoft,
            CorrelationId = "corr-existing",
            RequestedAtUtc = DateTime.UtcNow
        };

        // GetByProviderAndEmailAsync is called again in catch block
        accessRequestRepository
            .SetupSequence(repository => repository.GetByProviderAndEmailAsync("rider@example.com", IdentityProvider.Microsoft))
            .ReturnsAsync((PublicAccessRequestResponse?)null) // first call
            .ReturnsAsync(existing); // second call inside catch

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

        result.Created.Should().BeFalse();
        result.Response.Should().BeSameAs(existing);
        telemetryService.Verify(
            s => s.TrackOnboardingTransition("request-raced", "PublicRequest", "ExistingAfterRace", "corr-existing", It.IsAny<TimeSpan>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateOrGetExistingAsync_WhenCreateAsyncRacesAndRecordIsNull_ThrowsOriginalException() {
        var accessRequestRepository = new Mock<IAccessRequestRepository>();
        var notificationService = new Mock<IApproverNotificationService>();
        var correlationService = new Mock<ICorrelationService>();
        var telemetryService = new Mock<ITelemetryService>();

        correlationService.Setup(service => service.GetOrGenerateCorrelationId()).Returns("corr-raced");

        accessRequestRepository
            .Setup(repository => repository.GetByProviderAndEmailAsync("rider@example.com", IdentityProvider.Microsoft))
            .ReturnsAsync((PublicAccessRequestResponse?)null);

        accessRequestRepository
            .Setup(repository => repository.CreateAsync(It.IsAny<CreateAccessRequestRequest>(), "corr-raced"))
            .ThrowsAsync(new InvalidOperationException("Original error"));

        // GetByProviderAndEmailAsync returns null inside catch block too
        accessRequestRepository
            .Setup(repository => repository.GetByProviderAndEmailAsync("rider@example.com", IdentityProvider.Microsoft))
            .ReturnsAsync((PublicAccessRequestResponse?)null);

        var service = new AccessRequestService(
            accessRequestRepository.Object,
            notificationService.Object,
            correlationService.Object,
            telemetryService.Object,
            Options.Create(new OnboardingOptions()),
            new Mock<ILogger<AccessRequestService>>().Object);

        var act = () => service.CreateOrGetExistingAsync(new CreateAccessRequestRequest {
            Email = "rider@example.com",
            Provider = IdentityProvider.Microsoft
        });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Original error");
    }
}
