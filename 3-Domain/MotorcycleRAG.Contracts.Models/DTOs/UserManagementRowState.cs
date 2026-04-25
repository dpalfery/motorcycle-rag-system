using System.Text.Json.Serialization;

namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Derived state surfaced in the unified admin user-management table.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UserManagementRowState {
    PendingApproval,
    OnboardingInProgress,
    OnboardingFailed,
    Active,
    Cancelled
}