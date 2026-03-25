namespace MotorcycleRAG.Contracts.Models.DTOs;

public enum AgentRunState
{
    Queued,
    InProgress,
    RequiresAction,
    Completed,
    Failed,
    Cancelled,
    Expired
}
