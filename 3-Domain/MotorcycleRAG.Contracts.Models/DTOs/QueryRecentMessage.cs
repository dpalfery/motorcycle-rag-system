namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Recent conversation message sent with a query for lightweight follow-up context.
/// </summary>
public sealed class QueryRecentMessage
{
    public string Role { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;
}
