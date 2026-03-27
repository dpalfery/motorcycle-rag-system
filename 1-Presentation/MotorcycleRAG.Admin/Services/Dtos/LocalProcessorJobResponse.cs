using System.Text.Json.Serialization;

namespace MotorcycleRAG.Admin.Services.Dtos;

internal sealed class LocalProcessorJobResponse
{
    [JsonPropertyName("job_id")]
    public string JobId { get; init; } = string.Empty;

    [JsonPropertyName("upload_id")]
    public string UploadId { get; init; } = string.Empty;

    [JsonPropertyName("document_type")]
    public string DocumentType { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("progress")]
    public double Progress { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAtUtc { get; init; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAtUtc { get; init; }

    [JsonPropertyName("nodes_created")]
    public int? NodesCreated { get; init; }

    [JsonPropertyName("edges_created")]
    public int? EdgesCreated { get; init; }
}
