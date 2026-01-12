namespace MotorcycleRAG.Admin.Models.Api;

/// <summary>
/// Response for cancel pipeline operation
/// </summary>
#pragma warning disable CA1812 // Instantiated via deserialization
#pragma warning disable S3059 // Public properties required for serialization
internal class CancelPipelineResponse
{
    public string ExecutionId { get; set; } = string.Empty;
#pragma warning restore S3059
    public bool Cancelled { get; set; }
}
