using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Application.Services.Telemetry;
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
    private readonly DegradedModeTracker _degradedModeTracker;

    public OrchestratorToolHandlers(
        IFoundryAgentRunner runner,
        FoundryToolDispatcher subAgentDispatcher,
        IOptions<AzureFoundryOptions> options,
        ILogger<OrchestratorToolHandlers> logger,
        DegradedModeTracker degradedModeTracker)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(subAgentDispatcher);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(degradedModeTracker);

        _runner = runner;
        _subAgentDispatcher = subAgentDispatcher;
        _options = options.Value;
        _logger = logger;
        _degradedModeTracker = degradedModeTracker;
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
        => RunSubAgentAsync(call, _options.VectorSearchAgentName, "vector_search", ct);

    // -------------------------------------------------------------------------
    // web_search
    // -------------------------------------------------------------------------

    public Task<AgentToolOutput> HandleWebSearchAsync(AgentToolCall call, CancellationToken ct)
        => RunSubAgentAsync(call, _options.WebSearchAgentName, "web_search", ct);

    // -------------------------------------------------------------------------
    // pdf_search
    // -------------------------------------------------------------------------

    public Task<AgentToolOutput> HandlePDFSearchAsync(AgentToolCall call, CancellationToken ct)
        => RunSubAgentAsync(call, _options.PDFSearchAgentName, "pdf_search", ct);

    // -------------------------------------------------------------------------
    // Core sub-agent run loop
    // -------------------------------------------------------------------------

    private async Task<AgentToolOutput> RunSubAgentAsync(
        AgentToolCall call,
        string agentName,
        string toolName,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(agentName))
        {
            _logger.LogError("Agent name for '{ToolName}' is not configured", toolName);
            return new AgentToolOutput(call.CallId,
                JsonSerializer.Serialize(new { error = $"Agent name for '{toolName}' is not configured" }));
        }

        var conversationId = await _runner.CreateConversationAsync(ct);
        _logger.LogInformation(
            "Sub-agent response started: tool={Tool} agentName={AgentName} subConversationId={ConversationId}",
            toolName, agentName, conversationId);

        try
        {
            // Pass the entire tool call arguments as the user message to the sub-agent
            var status = await _runner.SendAgentMessageAsync(conversationId, agentName, call.ArgumentsJson, ct);
            var rounds = 0;

            while (status.State == AgentRunState.RequiresAction && rounds < MaxSubAgentRounds)
            {
                rounds++;
                _logger.LogDebug(
                    "Sub-response {ResponseId} on conversation {ConversationId}: RequiresAction (round {Round}), dispatching {Count} tool calls",
                    status.ResponseId, conversationId, rounds, status.RequiredToolCalls?.Count ?? 0);

                var subOutputs = await _subAgentDispatcher.DispatchAsync(
                    status.RequiredToolCalls ?? [], ct);

                status = await _runner.SubmitToolOutputsAsync(
                    conversationId, agentName, subOutputs, ct);
            }

            if (status.State != AgentRunState.Completed)
            {
                var errorMsg = $"Sub-agent run ended with state {status.State}";
                _logger.LogWarning(
                    "Sub-agent response ended in non-completed state {State} (tool={Tool}, responseId={ResponseId})",
                    status.State, toolName, status.ResponseId);
                _degradedModeTracker.TrackFoundrySubRunResult(toolName, succeeded: false, errorMessage: errorMsg);
                return new AgentToolOutput(call.CallId,
                    JsonSerializer.Serialize(new { error = errorMsg }));
            }

            _logger.LogInformation(
                "Sub-agent response completed: tool={Tool} responseId={ResponseId} answerLength={Length}",
                toolName, status.ResponseId, status.OutputText.Length);
            _degradedModeTracker.TrackFoundrySubRunResult(toolName, succeeded: true, resultSize: status.OutputText.Length);

            return new AgentToolOutput(call.CallId,
                JsonSerializer.Serialize(new { result = status.OutputText }));
        }
        finally
        {
            await SafeDeleteConversationAsync(conversationId, toolName, ct);
        }
    }

    private async Task SafeDeleteConversationAsync(string conversationId, string toolName, CancellationToken ct)
    {
        try
        {
            await _runner.DeleteConversationAsync(conversationId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to delete sub-agent conversation {ConversationId} for tool '{ToolName}'",
                conversationId, toolName);
        }
    }
}
