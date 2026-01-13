using System.Text.Json.Serialization;

namespace MotorcycleRAG.API.Services;

/// <summary>
/// Health check response model.
/// </summary>
internal sealed class HealthCheckResponse
{
    [JsonPropertyName("status")]
    internal string? Status { get; set; }

    [JsonPropertyName("totalDuration")]
    internal string? TotalDuration { get; set; }

    [JsonPropertyName("checks")]
    internal Dictionary<string, HealthCheckEntry>? Checks { get; set; }
}
