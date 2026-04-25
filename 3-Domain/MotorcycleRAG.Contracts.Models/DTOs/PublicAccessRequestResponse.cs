namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Public response returned to the login experience for access-request status.
/// </summary>
public class PublicAccessRequestResponse {
    public string RequestId { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public IdentityProvider Provider { get; set; }

    public string RequesterVisibleStatus { get; set; } = string.Empty;

    public string StatusMessage { get; set; } = string.Empty;

    public string NextAction { get; set; } = string.Empty;

    public RequestDecisionState RequestDecisionState { get; set; }

    public OnboardingExecutionState OnboardingExecutionState { get; set; }

    public UserManagementRowState RowState { get; set; }

    public DateTime RequestedAtUtc { get; set; }

    public string CorrelationId { get; set; } = string.Empty;

    public string RowVersion { get; set; } = string.Empty;
}