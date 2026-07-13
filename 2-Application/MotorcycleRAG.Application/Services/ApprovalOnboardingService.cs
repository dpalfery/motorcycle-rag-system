using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Application.Services;

/// <summary>
/// Coordinates approval-time onboarding across internal user, usage, identity, and request persistence.
/// </summary>
public class ApprovalOnboardingService {
    private readonly IAccessRequestRepository _accessRequestRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUserIdentityRepository _userIdentityRepository;
    private readonly IPlanRepository _planRepository;
    private readonly IUsageTrackingService _usageTrackingService;
    private readonly IExternalIdentityProvisioningService _externalIdentityProvisioningService;
    private readonly TierEntitlementMappingService _tierEntitlementMappingService;
    private readonly ITelemetryService _telemetryService;
    private readonly ILogger<ApprovalOnboardingService> _logger;

    public ApprovalOnboardingService(
        IAccessRequestRepository accessRequestRepository,
        IUserRepository userRepository,
        IUserIdentityRepository userIdentityRepository,
        IPlanRepository planRepository,
        IUsageTrackingService usageTrackingService,
        IExternalIdentityProvisioningService externalIdentityProvisioningService,
        TierEntitlementMappingService tierEntitlementMappingService,
        ITelemetryService telemetryService,
        ILogger<ApprovalOnboardingService> logger) {
        _accessRequestRepository = accessRequestRepository ?? throw new ArgumentNullException(nameof(accessRequestRepository));
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _userIdentityRepository = userIdentityRepository ?? throw new ArgumentNullException(nameof(userIdentityRepository));
        _planRepository = planRepository ?? throw new ArgumentNullException(nameof(planRepository));
        _usageTrackingService = usageTrackingService ?? throw new ArgumentNullException(nameof(usageTrackingService));
        _externalIdentityProvisioningService = externalIdentityProvisioningService ?? throw new ArgumentNullException(nameof(externalIdentityProvisioningService));
        _tierEntitlementMappingService = tierEntitlementMappingService ?? throw new ArgumentNullException(nameof(tierEntitlementMappingService));
        _telemetryService = telemetryService ?? throw new ArgumentNullException(nameof(telemetryService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes approval-time onboarding for an already-approved request in onboarding-in-progress state.
    /// </summary>
    public virtual async Task ExecuteAsync(AccessRequestAdminRecord accessRequest) {
        ArgumentNullException.ThrowIfNull(accessRequest);

        if (accessRequest.AssignedTier is null) {
            throw new InvalidOperationException($"Access request {accessRequest.RequestId} does not have an assigned tier.");
        }

        string stage = "ResolveTier";
        string? managedUserId = accessRequest.ManagedUserId;
        string? externalDirectoryObjectId = accessRequest.ExternalDirectoryObjectId;
        var stopwatch = Stopwatch.StartNew();

        try {
            var tier = accessRequest.AssignedTier.Value;
            var (planName, _) = _tierEntitlementMappingService.Resolve(tier);

            stage = "ResolvePlan";
            var plan = await _planRepository.GetPlanByNameAsync(planName)
                ?? throw new InvalidOperationException($"Plan {planName} is not configured.");

            stage = "UpsertManagedUser";
            var user = await GetOrCreateManagedUserAsync(accessRequest, plan.Id, tier);
            managedUserId = user.Id;

            stage = "SeedUsage";
            await _usageTrackingService.SeedOnboardingAccessAsync(user.Id, accessRequest.RequestId);

            stage = "ProvisionExternalIdentity";
            externalDirectoryObjectId = await _externalIdentityProvisioningService.ProvisionApprovedUserAsync(
                user.Email,
                string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email : user.DisplayName,
                tier,
                accessRequest.Provider);

            stage = "PersistIdentityLink";
            var identitySaved = await _userIdentityRepository.UpsertAsync(
                user.Id,
                accessRequest.Provider,
                user.Email,
                issuer: null,
                subject: null,
                providerUserId: null,
                externalDirectoryObjectId: externalDirectoryObjectId);

            if (!identitySaved) {
                throw new InvalidOperationException($"Identity link could not be persisted for managed user {user.Id}.");
            }

            stage = "CompleteRequest";
            var completed = await _accessRequestRepository.CompleteOnboardingAsync(accessRequest.RequestId, user.Id, externalDirectoryObjectId);
            if (completed == null) {
                throw new InvalidOperationException($"Access request {accessRequest.RequestId} could not be marked as completed.");
            }

            _logger.LogInformation(
                "Completed approval-time onboarding for access request {RequestId} and managed user {ManagedUserId}",
                accessRequest.RequestId,
                user.Id);
            _telemetryService.TrackOnboardingTransition(
                accessRequest.RequestId,
                stage,
                "Completed",
                accessRequest.CorrelationId,
                stopwatch.Elapsed);
        }
        catch (Exception ex) {
            await MarkFailedAsync(accessRequest.RequestId, stage, ex.Message, managedUserId, externalDirectoryObjectId);
            _telemetryService.TrackOnboardingTransition(
                accessRequest.RequestId,
                stage,
                "Failed",
                accessRequest.CorrelationId,
                stopwatch.Elapsed);

            if (stage == "ProvisionExternalIdentity") {
                _telemetryService.TrackDependencyDegradation(
                    "ExternalIdentityProvisioning",
                    accessRequest.CorrelationId,
                    ex.GetType().Name);
            }

            throw;
        }
    }

    private async Task<UserDTO> GetOrCreateManagedUserAsync(AccessRequestAdminRecord accessRequest, string planId, TierLabel tier) {
        var user = !string.IsNullOrWhiteSpace(accessRequest.ManagedUserId)
            ? await _userRepository.GetUserByIdAsync(accessRequest.ManagedUserId)
            : await _userRepository.GetUserByEmailAsync(accessRequest.Email);

        if (user == null) {
            var createdUser = new UserDTO {
                Email = accessRequest.Email,
                DisplayName = accessRequest.Email,
                IsEnabled = true,
                PlanId = planId,
                TierLabel = tier,
                AccessState = ManagedUserAccessState.Active,
                AuthProvider = accessRequest.Provider.ToString(),
                ProviderUserId = string.Empty,
                CreatedDate = DateTime.UtcNow,
                LastUpdatedDate = DateTime.UtcNow
            };

            return await _userRepository.CreateUserAsync(createdUser);
        }

        user.Email = accessRequest.Email;
        user.DisplayName = string.IsNullOrWhiteSpace(user.DisplayName) ? accessRequest.Email : user.DisplayName;
        user.IsEnabled = true;
        user.PlanId = planId;
        user.TierLabel = tier;
        user.AccessState = ManagedUserAccessState.Active;
        user.CancelledAtUtc = null;
        user.CancelledByUserId = null;
        user.CancelReason = null;
        user.AuthProvider = accessRequest.Provider.ToString();
        user.LastUpdatedDate = DateTime.UtcNow;

        var updated = await _userRepository.UpdateUserAsync(user);
        if (!updated) {
            throw new InvalidOperationException($"Managed user {user.Id} could not be updated during approval onboarding.");
        }

        return user;
    }

    private async Task MarkFailedAsync(
        string requestId,
        string stage,
        string message,
        string? managedUserId,
        string? externalDirectoryObjectId) {
        try {
            await _accessRequestRepository.FailOnboardingAsync(
                requestId,
                failureCode: stage,
                failureMessage: Truncate(message, 1000),
                managedUserId: managedUserId,
                externalDirectoryObjectId: externalDirectoryObjectId);

            _logger.LogWarning(
                "Marked access request {RequestId} as failed during stage {Stage}",
                requestId,
                stage);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Failed to persist onboarding failure state for access request {RequestId}", requestId);
        }
    }

    private static string Truncate(string value, int maxLength) {
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
