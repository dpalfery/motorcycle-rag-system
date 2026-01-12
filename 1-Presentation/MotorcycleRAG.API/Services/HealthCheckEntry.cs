using System.Text.Json.Serialization;

namespace MotorcycleRAG.API.Services;

/// <summary>
/// Individual health check entry.
/// </summary>
internal sealed class HealthCheckEntry
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("duration")]
    public string? Duration { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("data")]
    public IReadOnlyDictionary<string, object>? Data { get; set; }
}
