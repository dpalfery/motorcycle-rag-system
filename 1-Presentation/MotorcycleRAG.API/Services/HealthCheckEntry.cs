using System.Text.Json.Serialization;

namespace MotorcycleRAG.API.Services;

/// <summary>
/// Individual health check entry.
/// </summary>
internal sealed class HealthCheckEntry
{
    [JsonPropertyName("status")]
    internal string? Status { get; set; }

    [JsonPropertyName("duration")]
    internal string? Duration { get; set; }

    [JsonPropertyName("description")]
    internal string? Description { get; set; }

    [JsonPropertyName("data")]
    internal IReadOnlyDictionary<string, object>? Data { get; set; }
}
