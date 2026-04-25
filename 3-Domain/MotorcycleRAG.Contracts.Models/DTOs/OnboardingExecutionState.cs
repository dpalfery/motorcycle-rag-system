using System.Text.Json.Serialization;

namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Execution state for approval-time onboarding.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OnboardingExecutionState {
    NotStarted,
    InProgress,
    Failed,
    Completed,
    NotRequired
}