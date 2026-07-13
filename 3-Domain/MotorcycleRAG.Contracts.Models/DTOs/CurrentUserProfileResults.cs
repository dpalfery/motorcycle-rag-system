namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>Outcome of a current-user profile use case.</summary>
public sealed record CurrentUserProfileResult(
    CurrentUserProfileStatus Status,
    UserProfileResponse? Profile = null,
    IReadOnlyList<string>? ValidationErrors = null);

/// <summary>Outcome of a current-user usage use case.</summary>
public sealed record CurrentUserUsageResult(
    CurrentUserProfileStatus Status,
    UsageResponse? Usage = null);

/// <summary>Current-user access and validation statuses independent of HTTP.</summary>
public enum CurrentUserProfileStatus
{
    Success,
    Unauthenticated,
    AccessNotApproved,
    ValidationFailed
}
