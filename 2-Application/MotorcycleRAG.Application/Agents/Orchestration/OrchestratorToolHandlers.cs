using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using System.Text.Json;

namespace MotorcycleRAG.Application.Agents.Orchestration;

/// <summary>
/// Handler methods for the OrchestratorAgent's three tool calls:
/// <c>vector_search</c>, <c>web_search</c>, <c>pdf_search</c>.
/// Each handler starts a sub-agent run on the corresponding Foundry sub-agent,
/// drives the sub-agent run loop (including its own tool dispatch via <see cref="SubAgentToolHandlers"/>),
/// extracts the final sub-agent message, and returns it as a JSON tool output.
/// </summary>
public sealed class OrchestratorToolHandlers
{
    private const int MaxSubAgentRounds = 8;

    private readonly IFoundryAgentRunner _runner;
    private readonly FoundryToolDispatcher _subAgentDispatcher;
    private readonly AzureFoundryOptions _options;
    private readonly ILogger<OrchestratorToolHandlers> _logger;

    public OrchestratorToolHandlers(
        IFoundryAgentRunner runner,
        FoundryToolDispatcher subAgentDispatcher,
        IOptions<AzureFoundryOptions> options,
        ILogger<OrchestratorToolHandlers> logger)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(subAgentDispatcher);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _runner = runner;
        _subAgentDispatcher = subAgentDispatcher;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Registers the three orchestrator tool handlers on <paramref name="dispatcher"/>.
    /// </summary>
    public void RegisterOn(FoundryToolDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        dispatcher.RegisterHandler("vector_search", HandleVectorSearchAsync);
        dispatcher.RegisterHandler("web_search", HandleWebSearchAsync);
        dispatcher.RegisterHandler("pdf_search", HandlePDFSearchAsync);
    }

    // -------------------------------------------------------------------------
    // vector_search
    // -------------------------------------------------------------------------

    public Task<AgentToolOutput> HandleVectorSearchAsync(AgentToolCall call, CancellationToken ct)
        => RunSubAgentAsync(call, _options.VectorSearchAgentId, "vector_search", ct);

    // -------------------------------------------------------------------------
    // web_search
    // -------------------------------------------------------------------------

    public Task<AgentToolOutput> HandleWebSearchAsync(AgentToolCall call, CancellationToken ct)
        => RunSubAgentAsync(call, _options.WebSearchAgentId, "web_search", ct);

    // -------------------------------------------------------------------------
    // pdf_search
    // -------------------------------------------------------------------------

    public Task<AgentToolOutput> HandlePDFSearchAsync(AgentToolCall call, CancellationToken ct)
        => RunSubAgentAsync(call, _options.PDFSearchAgentId, "pdf_search", ct);

    // -------------------------------------------------------------------------
    // Core sub-agent run loop
    // -------------------------------------------------------------------------

    private async Task<AgentToolOutput> RunSubAgentAsync(
        AgentToolCall call,
        string agentId,
        string toolName,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            _logger.LogError("Agent ID for '{ToolName}' is not configured", toolName);
            return new AgentToolOutput(call.CallId,
                JsonSerializer.Serialize(new { error = $"Agent ID for '{toolName}' is not configured" }));
        }

        var threadId = await _runner.CreateThreadAsync(ct);
        _logger.LogInformation(
            "Sub-agent run started: tool={Tool} agentId={AgentId} subThreadId={ThreadId}",
            toolName, agentId, threadId);

        try
        {
            // Pass the entire tool call arguments as the user message to the sub-agent
            await _runner.AddUserMessageAsync(threadId, call.ArgumentsJson, ct);

            var status = await _runner.CreateRunAsync(threadId, agentId, ct);
            var rounds = 0;

            while (status.State == AgentRunState.RequiresAction && rounds < MaxSubAgentRounds)
            {
                rounds++;
                _logger.LogDebug(
                    "Sub-run {RunId} on thread {ThreadId}: RequiresAction (round {Round}), dispatching {Count} tool calls",
                    status.RunId, threadId, rounds, status.RequiredToolCalls?.Count ?? 0);

                var subOutputs = await _subAgentDispatcher.DispatchAsync(
                    status.RequiredToolCalls ?? [], ct);

                status = await _runner.SubmitToolOutputsAsync(
                    threadId, status.RunId, subOutputs, ct);
            }

            if (status.State != AgentRunState.Completed)
            {
                _logger.LogWarning(
                    "Sub-agent run ended in non-completed state {State} (tool={Tool}, subRunId={RunId})",
                    status.State, toolName, status.RunId);
                return new AgentToolOutput(call.CallId,
                    JsonSerializer.Serialize(new { error = $"Sub-agent run ended with state {status.State}" }));
            }

            var answer = await _runner.GetLastAssistantMessageAsync(threadId, ct);
            _logger.LogInformation(
                "Sub-agent run completed: tool={Tool} subRunId={RunId} answerLength={Length}",
                toolName, status.RunId, answer.Length);

            return new AgentToolOutput(call.CallId,
                JsonSerializer.Serialize(new { result = answer }));
        }
        finally
        {
            await SafeDeleteThreadAsync(threadId, toolName, ct);
        }
    }

    private async Task SafeDeleteThreadAsync(string threadId, string toolName, CancellationToken ct)
    {
        try
        {
            await _runner.DeleteThreadAsync(threadId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to delete sub-agent thread {ThreadId} for tool '{ToolName}'",
                threadId, toolName);
        }
    }
}
