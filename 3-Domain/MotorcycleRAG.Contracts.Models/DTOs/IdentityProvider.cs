using System.Text.Json.Serialization;

namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Supported public identity providers for onboarding requests.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IdentityProvider {
    Microsoft,
    Google
}