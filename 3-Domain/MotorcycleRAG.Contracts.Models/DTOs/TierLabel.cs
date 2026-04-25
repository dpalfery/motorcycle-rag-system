using System.Text.Json.Serialization;

namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Supported onboarding tier labels.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TierLabel {
    Trial,
    RoadRunner,
    Admin
}