using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Admin.Models.Api;

/// <summary>
/// Response for get pipeline status operation
/// </summary>
#pragma warning disable CA1812 // Instantiated via deserialization
#pragma warning disable S3059 // Public properties required for serialization
internal class PipelineStatusResponse
{
    public string ExecutionId { get; set; } = string.Empty;
#pragma warning restore S3059
    public PipelineStatus Status { get; set; }
}
