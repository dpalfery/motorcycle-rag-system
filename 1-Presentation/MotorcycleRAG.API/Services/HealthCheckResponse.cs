using System.Text.Json.Serialization;

namespace MotorcycleRAG.API.Services;

/// <summary>
/// Health check response model.
/// </summary>
internal sealed class HealthCheckResponse
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("totalDuration")]
    public string? TotalDuration { get; set; }

    [JsonPropertyName("checks")]
    public Dictionary<string, HealthCheckEntry>? Checks { get; set; }
}
