using System.Text.Json.Serialization;

namespace MotorcycleRAG.Domain.DTOs;

/// <summary>
/// File types supported by the pipeline
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FileType
{
    CSV,
    PDF,
    Unknown
}
