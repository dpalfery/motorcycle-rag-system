namespace MotorcycleRAG.Admin.Services.Dtos;

#pragma warning disable CA1812 // Instantiated via deserialization
#pragma warning disable S3059 // Public properties required for serialization
internal class McpToolConfigurationDto
{
    public Guid Id { get; set; }
#pragma warning restore S3059
    public string ToolId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Uri ServerUrl { get; set; } = default!;
    public string ToolType { get; set; } = string.Empty;
    public string? Version { get; set; }
    public bool IsEnabled { get; set; }
}
