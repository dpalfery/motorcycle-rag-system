namespace MotorcycleRAG.Contracts.Models.DTOs;

public record AgentResponseStatus(
    string ResponseId,
    AgentRunState State,
    IReadOnlyList<AgentToolCall>? RequiredToolCalls,
    string OutputText);
