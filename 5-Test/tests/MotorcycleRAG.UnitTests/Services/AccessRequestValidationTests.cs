using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.UnitTests.Services;

public class AccessRequestValidationTests {
    private readonly Mock<IAccessRequestRepository> _accessRequestRepository = new();
    private readonly Mock<IApproverNotificationService> _approverNotificationService = new();
    private readonly Mock<ICorrelationService> _correlationService = new();
    private readonly Mock<ILogger<AccessRequestService>> _logger = new();

    [Fact]
    public async Task CreateOrGetExistingAsync_WhenExistingRequestExists_ReturnsExistingState() {
        var existingRequest = new PublicAccessRequestResponse {
            RequestId = Guid.NewGuid().ToString(),
            Email = "rider@example.com",
            Provider = IdentityProvider.Microsoft,
            RequesterVisibleStatus = "PendingReview",
            RequestDecisionState = RequestDecisionState.Pending,
            OnboardingExecutionState = OnboardingExecutionState.NotStarted,
            RowState = UserManagementRowState.PendingApproval,
            RequestedAtUtc = DateTime.UtcNow
        };

        _accessRequestRepository
            .Setup(repository => repository.GetByProviderAndEmailAsync("rider@example.com", IdentityProvider.Microsoft))
            .ReturnsAsync(existingRequest);

        var service = CreateService();

        var result = await service.CreateOrGetExistingAsync(new CreateAccessRequestRequest {
            Email = " Rider@Example.com ",
            Provider = IdentityProvider.Microsoft
        });

        result.Created.Should().BeFalse();
        result.Response.Should().BeEquivalentTo(existingRequest);
        _accessRequestRepository.Verify(repository => repository.CreateAsync(It.IsAny<CreateAccessRequestRequest>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CreateOrGetExistingAsync_WhenNewRequestIsCreated_NotifiesApprover() {
        _correlationService.Setup(service => service.GetOrGenerateCorrelationId()).Returns("corr-001");

        var createdRequest = new PublicAccessRequestResponse {
            RequestId = Guid.NewGuid().ToString(),
            Email = "rider@example.com",
            Provider = IdentityProvider.Google,
            RequesterVisibleStatus = "PendingReview",
            RequestDecisionState = RequestDecisionState.Pending,
            OnboardingExecutionState = OnboardingExecutionState.NotStarted,
            RowState = UserManagementRowState.PendingApproval,
            RequestedAtUtc = DateTime.UtcNow,
            CorrelationId = "corr-001"
        };

        _accessRequestRepository
            .Setup(repository => repository.GetByProviderAndEmailAsync("rider@example.com", IdentityProvider.Google))
            .ReturnsAsync((PublicAccessRequestResponse?)null);
        _accessRequestRepository
            .Setup(repository => repository.CreateAsync(
                It.Is<CreateAccessRequestRequest>(request => request.Email == "rider@example.com" && request.Provider == IdentityProvider.Google),
                "corr-001"))
            .ReturnsAsync(createdRequest);

        var service = CreateService("approver@example.com");

        var result = await service.CreateOrGetExistingAsync(new CreateAccessRequestRequest {
            Email = "rider@example.com",
            Provider = IdentityProvider.Google
        });

        result.Created.Should().BeTrue();
        result.Response.Should().BeEquivalentTo(createdRequest);
        _approverNotificationService.Verify(
            service => service.SendAccessRequestSubmittedAsync(createdRequest, "approver@example.com"),
            Times.Once);
    }

    [Fact]
    public async Task CreateOrGetExistingAsync_WhenNotificationFails_StillReturnsCreatedRequest() {
        _correlationService.Setup(service => service.GetOrGenerateCorrelationId()).Returns("corr-002");

        var createdRequest = new PublicAccessRequestResponse {
            RequestId = Guid.NewGuid().ToString(),
            Email = "rider@example.com",
            Provider = IdentityProvider.Microsoft,
            RequesterVisibleStatus = "PendingReview",
            RequestDecisionState = RequestDecisionState.Pending,
            OnboardingExecutionState = OnboardingExecutionState.NotStarted,
            RowState = UserManagementRowState.PendingApproval,
            RequestedAtUtc = DateTime.UtcNow,
            CorrelationId = "corr-002"
        };

        _accessRequestRepository
            .Setup(repository => repository.GetByProviderAndEmailAsync("rider@example.com", IdentityProvider.Microsoft))
            .ReturnsAsync((PublicAccessRequestResponse?)null);
        _accessRequestRepository
            .Setup(repository => repository.CreateAsync(It.IsAny<CreateAccessRequestRequest>(), "corr-002"))
            .ReturnsAsync(createdRequest);
        _approverNotificationService
            .Setup(service => service.SendAccessRequestSubmittedAsync(createdRequest, "approver@example.com"))
            .ThrowsAsync(new InvalidOperationException("mailbox unavailable"));

        var service = CreateService("approver@example.com");

        var result = await service.CreateOrGetExistingAsync(new CreateAccessRequestRequest {
            Email = "rider@example.com",
            Provider = IdentityProvider.Microsoft
        });

        result.Created.Should().BeTrue();
        result.Response.Should().BeEquivalentTo(createdRequest);
    }

    private AccessRequestService CreateService(string approverAddress = "") {
        return new AccessRequestService(
            _accessRequestRepository.Object,
            _approverNotificationService.Object,
            _correlationService.Object,
            Options.Create(new OnboardingOptions { ApproverAddress = approverAddress }),
            _logger.Object);
    }
}