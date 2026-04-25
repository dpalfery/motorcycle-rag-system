using System.Text.Json.Serialization;

namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Effective access state for a managed user.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ManagedUserAccessState {
    None,
    Active,
    Cancelled,
    Disabled
}