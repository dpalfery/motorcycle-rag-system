using System.Text.Json.Serialization;

namespace MotorcycleRAG.Admin.Services.Dtos;

internal sealed class LocalProcessorShutdownResponse
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("active_jobs")]
    public int ActiveJobs { get; init; }
}
