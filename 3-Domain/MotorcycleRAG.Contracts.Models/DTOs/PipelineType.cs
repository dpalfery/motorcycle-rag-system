using System.Text.Json.Serialization;

namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Pipeline type enumeration
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PipelineType {
    CSV,
    PDF,
    Batch,
    Scheduled
}
