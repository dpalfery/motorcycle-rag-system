using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Manages the conversation and response lifecycle for Microsoft Foundry Agent Service.
/// All LLM reasoning occurs inside Foundry; .NET code only drives response generation and executes I/O tool calls.
/// </summary>
public interface IFoundryAgentRunner
{
    /// <summary>Creates a new Foundry conversation. Returns the conversation ID.</summary>
    Task<string> CreateConversationAsync(CancellationToken ct = default);

    /// <summary>Sends a user message to an agent and returns the response status.</summary>
    Task<AgentResponseStatus> SendAgentMessageAsync(
        string conversationId,
        string agentName,
        string content,
        CancellationToken ct = default);

    /// <summary>
    /// Submits function tool outputs for a response that requested action, then returns the next response status.
    /// </summary>
    Task<AgentResponseStatus> SubmitToolOutputsAsync(
        string conversationId,
        string agentName,
        IEnumerable<AgentToolOutput> outputs,
        CancellationToken ct = default);

    /// <summary>Deletes the conversation and all its items to free Foundry resources.</summary>
    Task DeleteConversationAsync(string conversationId, CancellationToken ct = default);
}
