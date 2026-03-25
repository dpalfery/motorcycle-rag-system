namespace MotorcycleRAG.Contracts.Models.DTOs;

public record AgentToolCall(
    string CallId,
    string FunctionName,
    string ArgumentsJson);
