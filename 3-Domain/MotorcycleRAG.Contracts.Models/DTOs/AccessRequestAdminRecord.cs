namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Internal admin-facing access-request state used by orchestration and persistence layers.
/// </summary>
public class AccessRequestAdminRecord {
    public string RequestId { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public IdentityProvider Provider { get; set; }

    public RequestDecisionState RequestDecisionState { get; set; }

    public OnboardingExecutionState OnboardingExecutionState { get; set; }

    public TierLabel? AssignedTier { get; set; }

    public string? ManagedUserId { get; set; }

    public string? ExternalDirectoryObjectId { get; set; }

    public string CorrelationId { get; set; } = string.Empty;

    public DateTime RequestedAtUtc { get; set; }

    public DateTime? ApprovedAtUtc { get; set; }

    public DateTime? CancelledAtUtc { get; set; }

    public int OnboardingAttemptCount { get; set; }

    public string? LastFailureCode { get; set; }

    public string? LastFailureMessage { get; set; }

    public string RowVersion { get; set; } = string.Empty;
}