using System.Text.Json.Serialization;

namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Pipeline execution status
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PipelineStatus
{
    Queued,
    Processing,
    Indexing,
    Completed,
    Failed,
    Cancelled,
    PartiallyCompleted
}

