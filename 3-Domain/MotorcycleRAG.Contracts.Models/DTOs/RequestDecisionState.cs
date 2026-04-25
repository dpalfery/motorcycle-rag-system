using System.Text.Json.Serialization;

namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Decision state for an onboarding access request.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RequestDecisionState {
    Pending,
    Approved,
    Cancelled
}