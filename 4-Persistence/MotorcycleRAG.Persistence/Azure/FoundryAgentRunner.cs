using Azure.AI.Agents.Persistent;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.Persistence.Azure;

/// <summary>
/// Implements <see cref="IFoundryAgentRunner"/> using the Azure AI Agents Persistent SDK.
/// All LLM reasoning occurs inside Foundry; this class only drives the thread/run lifecycle
/// and executes I/O tool calls.
/// </summary>
public sealed class FoundryAgentRunner : IFoundryAgentRunner
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMilliseconds(500);
    private const int MaxPollIterations = 120; // 60 seconds max

    private readonly PersistentAgentsClient _agentsClient;
    private readonly ILogger<FoundryAgentRunner> _logger;

    public FoundryAgentRunner(
        IOptions<AzureFoundryOptions> options,
        ILogger<FoundryAgentRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        var config = options.Value ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(config.FoundryEndpoint))
            throw new InvalidOperationException("AzureAI:FoundryEndpoint is required for FoundryAgentRunner");

        _agentsClient = new PersistentAgentsClient(config.FoundryEndpoint, new DefaultAzureCredential());
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> CreateThreadAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("Creating Foundry thread");
        var thread = await _agentsClient.Threads.CreateThreadAsync(
            new AgentThreadCreationOptions(), ct);
        _logger.LogDebug("Created Foundry thread {ThreadId}", thread.Value.Id);
        return thread.Value.Id;
    }

    /// <inheritdoc />
    public async Task AddUserMessageAsync(string threadId, string content, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentNullException.ThrowIfNull(content);

        _logger.LogDebug("Adding user message to thread {ThreadId}", threadId);
        await _agentsClient.Messages.CreateMessageAsync(
            threadId,
            MessageRole.User,
            content,
            cancellationToken: ct);
    }

    /// <inheritdoc />
    public async Task<AgentRunStatus> CreateRunAsync(string threadId, string agentId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        _logger.LogDebug("Creating run on thread {ThreadId} for agent {AgentId}", threadId, agentId);
        var run = await _agentsClient.Runs.CreateRunAsync(
            threadId,
            new CreateRunOptions(agentId),
            ct);

        return await PollUntilTerminalOrRequiresActionAsync(threadId, run.Value, ct);
    }

    /// <inheritdoc />
    public async Task<AgentRunStatus> GetRunStatusAsync(string threadId, string runId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        var run = await _agentsClient.Runs.GetRunAsync(threadId, runId, ct);
        return MapToAgentRunStatus(run.Value);
    }

    /// <inheritdoc />
    public async Task<AgentRunStatus> SubmitToolOutputsAsync(
        string threadId,
        string runId,
        IEnumerable<AgentToolOutput> outputs,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(outputs);

        var toolOutputList = outputs
            .Select(o => new ToolOutput(o.CallId, o.Output))
            .ToList();

        _logger.LogDebug(
            "Submitting {Count} tool outputs for run {RunId} on thread {ThreadId}",
            toolOutputList.Count, runId, threadId);

        var run = await _agentsClient.Runs.SubmitToolOutputsToRunAsync(
            threadId, runId, toolOutputList, ct);

        return await PollUntilTerminalOrRequiresActionAsync(threadId, run.Value, ct);
    }

    /// <inheritdoc />
    public async Task<string> GetLastAssistantMessageAsync(string threadId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        _logger.LogDebug("Retrieving last assistant message from thread {ThreadId}", threadId);
        await foreach (var message in _agentsClient.Messages.GetMessagesAsync(threadId, cancellationToken: ct))
        {
            if (message.Role == MessageRole.Agent)
            {
                var text = message.ContentItems
                    .OfType<MessageTextContent>()
                    .FirstOrDefault()?.Text ?? string.Empty;
                _logger.LogDebug("Found assistant message ({Length} chars) in thread {ThreadId}", text.Length, threadId);
                return text;
            }
        }

        _logger.LogWarning("No assistant message found in thread {ThreadId}", threadId);
        return string.Empty;
    }

    /// <inheritdoc />
    public async Task DeleteThreadAsync(string threadId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        _logger.LogDebug("Deleting thread {ThreadId}", threadId);
        await _agentsClient.Threads.DeleteThreadAsync(threadId, ct);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<AgentRunStatus> PollUntilTerminalOrRequiresActionAsync(
        string threadId,
        ThreadRun run,
        CancellationToken ct)
    {
        var iterations = 0;
        while (IsActiveRunState(run.Status) && iterations < MaxPollIterations)
        {
            await Task.Delay(PollingInterval, ct);
            var updated = await _agentsClient.Runs.GetRunAsync(threadId, run.Id, ct);
            run = updated.Value;
            iterations++;

            _logger.LogDebug(
                "Run {RunId} on thread {ThreadId}: state={State} (iteration {Iteration})",
                run.Id, threadId, run.Status, iterations);
        }

        if (iterations >= MaxPollIterations)
        {
            _logger.LogWarning(
                "Run {RunId} did not reach terminal state within {MaxIterations} poll iterations",
                run.Id, MaxPollIterations);
        }

        return MapToAgentRunStatus(run);
    }

    private static bool IsActiveRunState(RunStatus status) =>
        status == RunStatus.Queued || status == RunStatus.InProgress;

    private static AgentRunStatus MapToAgentRunStatus(ThreadRun run)
    {
        var state = run.Status switch
        {
            RunStatus.Queued => AgentRunState.Queued,
            RunStatus.InProgress => AgentRunState.InProgress,
            RunStatus.RequiresAction => AgentRunState.RequiresAction,
            RunStatus.Completed => AgentRunState.Completed,
            RunStatus.Failed => AgentRunState.Failed,
            RunStatus.Cancelled => AgentRunState.Cancelled,
            RunStatus.Expired => AgentRunState.Expired,
            _ => AgentRunState.Failed
        };

        IReadOnlyList<AgentToolCall>? toolCalls = null;

        if (state == AgentRunState.RequiresAction && run.RequiredAction is SubmitToolOutputsAction submitAction)
        {
            toolCalls = submitAction.ToolCalls
                .Select(tc => new AgentToolCall(tc.Id, tc.FunctionName, tc.FunctionArguments))
                .ToList()
                .AsReadOnly();
        }

        return new AgentRunStatus(run.Id, state, toolCalls);
    }
}
