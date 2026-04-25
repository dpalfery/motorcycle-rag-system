using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Admin.Services.Dtos;

/// <summary>
/// Admin-app DTO for a unified user-management row.
/// </summary>
internal sealed class UserManagementRowDto {
    public string RowId { get; set; } = string.Empty;

    public string RowType { get; set; } = string.Empty;

    public string? AccessRequestId { get; set; }

    public string? ManagedUserId { get; set; }

    public string Email { get; set; } = string.Empty;

    public IdentityProvider Provider { get; set; }

    public TierLabel? AssignedTier { get; set; }

    public RequestDecisionState RequestDecisionState { get; set; }

    public OnboardingExecutionState OnboardingExecutionState { get; set; }

    public ManagedUserAccessState ManagedUserAccessState { get; set; }

    public UserManagementRowState RowState { get; set; }

    public string[] AllowedActions { get; set; } = Array.Empty<string>();

    public string CorrelationId { get; set; } = string.Empty;

    public DateTime? RequestedAtUtc { get; set; }

    public DateTime? ApprovedAtUtc { get; set; }

    public DateTime? CancelledAtUtc { get; set; }

    public string? LastFailureCode { get; set; }

    public string? LastFailureMessage { get; set; }

    public string RowVersion { get; set; } = string.Empty;
}