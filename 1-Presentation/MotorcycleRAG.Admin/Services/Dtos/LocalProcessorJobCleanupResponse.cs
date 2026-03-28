using System.Text.Json.Serialization;

namespace MotorcycleRAG.Admin.Services.Dtos;

internal sealed class LocalProcessorJobCleanupResponse
{
    [JsonPropertyName("deleted_count")]
    public int DeletedCount { get; init; }

    [JsonPropertyName("remaining_jobs")]
    public int RemainingJobs { get; init; }

    [JsonPropertyName("active_jobs")]
    public int ActiveJobs { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}
