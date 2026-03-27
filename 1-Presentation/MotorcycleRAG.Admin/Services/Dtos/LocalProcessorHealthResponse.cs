using System.Text.Json.Serialization;

namespace MotorcycleRAG.Admin.Services.Dtos;

internal sealed class LocalProcessorHealthResponse
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("accepting_work")]
    public bool AcceptingWork { get; init; }

    [JsonPropertyName("shutdown_requested")]
    public bool ShutdownRequested { get; init; }

    [JsonPropertyName("active_jobs")]
    public int ActiveJobs { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}
