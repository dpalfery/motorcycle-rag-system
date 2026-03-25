using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Manages the thread and run lifecycle for Azure AI Foundry Agent Service.
/// All LLM reasoning occurs inside Foundry; .NET code only drives the run loop and executes I/O tool calls.
/// </summary>
public interface IFoundryAgentRunner
{
    /// <summary>Creates a new conversation thread. Returns the thread ID.</summary>
    Task<string> CreateThreadAsync(CancellationToken ct = default);

    /// <summary>Appends a user message to an existing thread.</summary>
    Task AddUserMessageAsync(string threadId, string content, CancellationToken ct = default);

    /// <summary>
    /// Creates a run on <paramref name="agentId"/> within <paramref name="threadId"/> and polls
    /// until the run reaches a terminal state or <see cref="AgentRunState.RequiresAction"/>.
    /// </summary>
    Task<AgentRunStatus> CreateRunAsync(string threadId, string agentId, CancellationToken ct = default);

    /// <summary>Polls the current status of an existing run.</summary>
    Task<AgentRunStatus> GetRunStatusAsync(string threadId, string runId, CancellationToken ct = default);

    /// <summary>
    /// Submits tool outputs for a run that is in <see cref="AgentRunState.RequiresAction"/>,
    /// then polls until the next terminal state or <see cref="AgentRunState.RequiresAction"/>.
    /// </summary>
    Task<AgentRunStatus> SubmitToolOutputsAsync(
        string threadId,
        string runId,
        IEnumerable<AgentToolOutput> outputs,
        CancellationToken ct = default);

    /// <summary>Returns the last assistant message text from the thread.</summary>
    Task<string> GetLastAssistantMessageAsync(string threadId, CancellationToken ct = default);

    /// <summary>Deletes the thread and all its messages to free Foundry resources.</summary>
    Task DeleteThreadAsync(string threadId, CancellationToken ct = default);
}
