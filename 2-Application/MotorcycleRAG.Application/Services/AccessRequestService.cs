using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Core.Utilities;

namespace MotorcycleRAG.Application.Services;

/// <summary>
/// Handles public onboarding access-request submission, deduplication, and approver notification.
/// </summary>
public class AccessRequestService {
    private readonly IAccessRequestRepository _accessRequestRepository;
    private readonly IApproverNotificationService _approverNotificationService;
    private readonly ICorrelationService _correlationService;
    private readonly ITelemetryService _telemetryService;
    private readonly OnboardingOptions _onboardingOptions;
    private readonly ILogger<AccessRequestService> _logger;

    public AccessRequestService(
        IAccessRequestRepository accessRequestRepository,
        IApproverNotificationService approverNotificationService,
        ICorrelationService correlationService,
        ITelemetryService telemetryService,
        IOptions<OnboardingOptions> onboardingOptions,
        ILogger<AccessRequestService> logger) {
        _accessRequestRepository = accessRequestRepository ?? throw new ArgumentNullException(nameof(accessRequestRepository));
        _approverNotificationService = approverNotificationService ?? throw new ArgumentNullException(nameof(approverNotificationService));
        _correlationService = correlationService ?? throw new ArgumentNullException(nameof(correlationService));
        _telemetryService = telemetryService ?? throw new ArgumentNullException(nameof(telemetryService));
        _onboardingOptions = onboardingOptions?.Value ?? throw new ArgumentNullException(nameof(onboardingOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates a new access request or returns the current requester-visible state for an existing request.
    /// </summary>
    public virtual async Task<(PublicAccessRequestResponse Response, bool Created)> CreateOrGetExistingAsync(CreateAccessRequestRequest request) {
        ArgumentNullException.ThrowIfNull(request);

        var stopwatch = Stopwatch.StartNew();
        var normalizedEmail = NormalizeEmail(request.Email);
        var normalizedRequest = new CreateAccessRequestRequest {
            Email = normalizedEmail,
            Provider = request.Provider
        };

        var existingRequest = await _accessRequestRepository.GetByProviderAndEmailAsync(normalizedEmail, request.Provider);
        if (existingRequest != null) {
            _logger.LogInformation(
                "Returning existing access request state for {Provider}/{Email}",
                LogSanitizer.Sanitize(request.Provider),
                LogSanitizer.Sanitize(normalizedEmail));

            _telemetryService.TrackOnboardingTransition(
                existingRequest.RequestId,
                "PublicRequest",
                "Existing",
                existingRequest.CorrelationId,
                stopwatch.Elapsed);
            return (existingRequest, false);
        }

        var correlationId = _correlationService.GetOrGenerateCorrelationId();
        PublicAccessRequestResponse createdRequest;

        try {
            createdRequest = await _accessRequestRepository.CreateAsync(normalizedRequest, correlationId);
        }
        catch (InvalidOperationException ex) {
            _logger.LogWarning(
                ex,
                "Access-request creation raced with an existing request for {Provider}/{Email}",
                LogSanitizer.Sanitize(request.Provider),
                LogSanitizer.Sanitize(normalizedEmail));

            existingRequest = await _accessRequestRepository.GetByProviderAndEmailAsync(normalizedEmail, request.Provider);
            if (existingRequest != null) {
                _telemetryService.TrackOnboardingTransition(
                    existingRequest.RequestId,
                    "PublicRequest",
                    "ExistingAfterRace",
                    existingRequest.CorrelationId,
                    stopwatch.Elapsed);
                return (existingRequest, false);
            }

            throw;
        }

        await TryNotifyApproverAsync(createdRequest);
        _telemetryService.TrackOnboardingTransition(
            createdRequest.RequestId,
            "PublicRequest",
            "Accepted",
            createdRequest.CorrelationId,
            stopwatch.Elapsed);

        _telemetryService.TrackMetric(
            "AccessRequestAcceptedToManagementVisibleMs",
            0,
            new Dictionary<string, string> {
                ["RequestId"] = createdRequest.RequestId,
                ["Provider"] = createdRequest.Provider.ToString(),
                ["CorrelationId"] = createdRequest.CorrelationId
            });

        return (createdRequest, true);
    }

    private async Task TryNotifyApproverAsync(PublicAccessRequestResponse accessRequest) {
        var approverAddress = _onboardingOptions.ApproverAddress?.Trim();
        if (string.IsNullOrWhiteSpace(approverAddress)) {
            _logger.LogWarning(
                "Approver notification skipped for access request {RequestId} because Onboarding:ApproverAddress is not configured",
                LogSanitizer.Sanitize(accessRequest.RequestId));
            _telemetryService.TrackDependencyDegradation(
                "ApproverNotification",
                accessRequest.CorrelationId,
                "Onboarding:ApproverAddress is not configured");
            return;
        }

        try {
            await _approverNotificationService.SendAccessRequestSubmittedAsync(accessRequest, approverAddress);
        }
        catch (Exception ex) {
            _logger.LogError(
                ex,
                "Approver notification failed for access request {RequestId} and correlation {CorrelationId}",
                LogSanitizer.Sanitize(accessRequest.RequestId),
                LogSanitizer.Sanitize(accessRequest.CorrelationId, 80));
            _telemetryService.TrackDependencyDegradation(
                "ApproverNotification",
                accessRequest.CorrelationId,
                ex.GetType().Name);
        }
    }

    private static string NormalizeEmail(string email) {
        if (string.IsNullOrWhiteSpace(email)) {
            throw new ArgumentException("Email cannot be null or empty", nameof(email));
        }

        return email.Trim().ToLowerInvariant();
    }
}
