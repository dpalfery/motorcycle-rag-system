namespace MotorcycleRAG.Contracts.Models.DTOs;

public record AgentRunStatus(
    string RunId,
    AgentRunState State,
    IReadOnlyList<AgentToolCall>? RequiredToolCalls);
