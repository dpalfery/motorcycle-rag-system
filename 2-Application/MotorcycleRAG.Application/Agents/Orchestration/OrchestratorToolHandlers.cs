using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Application.Services.QueryValidation;
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
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IFoundryAgentRunner _runner;
    private readonly FoundryToolDispatcher _subAgentDispatcher;
    private readonly AzureFoundryOptions _options;
    private readonly ILogger<OrchestratorToolHandlers> _logger;
    private readonly DegradedModeTracker _degradedModeTracker;
    private readonly QuestionValidationService _questionValidationService;
    private readonly QuestionValidationState _questionValidationState;

    public OrchestratorToolHandlers(
        IFoundryAgentRunner runner,
        FoundryToolDispatcher subAgentDispatcher,
        IOptions<AzureFoundryOptions> options,
        ILogger<OrchestratorToolHandlers> logger,
        DegradedModeTracker degradedModeTracker,
        QuestionValidationService questionValidationService,
        QuestionValidationState questionValidationState)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(subAgentDispatcher);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(degradedModeTracker);
        ArgumentNullException.ThrowIfNull(questionValidationService);
        ArgumentNullException.ThrowIfNull(questionValidationState);

        _runner = runner;
        _subAgentDispatcher = subAgentDispatcher;
        _options = options.Value;
        _logger = logger;
        _degradedModeTracker = degradedModeTracker;
        _questionValidationService = questionValidationService;
        _questionValidationState = questionValidationState;
    }

    /// <summary>
    /// Registers the three orchestrator tool handlers on <paramref name="dispatcher"/>.
    /// </summary>
    public void RegisterOn(FoundryToolDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        dispatcher.RegisterHandler("validate_question", HandleValidateQuestionAsync);
        dispatcher.RegisterHandler("vector_search", HandleVectorSearchAsync);
        dispatcher.RegisterHandler("web_search", HandleWebSearchAsync);
        dispatcher.RegisterHandler("pdf_search", HandlePDFSearchAsync);
        dispatcher.RegisterHandler("graph_query", HandleGraphQueryAsync);
    }

    public async Task<AgentToolOutput> HandleValidateQuestionAsync(AgentToolCall call, CancellationToken ct)
    {
        var args = ParseArgs(call.ArgumentsJson);
        var query = args.TryGetProperty("query", out var q) ? q.GetString() ?? string.Empty : string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            query = _questionValidationState.OriginalQuery;
        }

        _logger.LogInformation("validate_question started: queryLength={QueryLength}", query.Length);
        var result = await _questionValidationService
            .ValidateAsync(query, _questionValidationState.RecentMessages, ct)
            .ConfigureAwait(false);

        _questionValidationState.Record(result);

        _logger.LogInformation(
            "validate_question completed: subject={Subject} maySearch={MaySearch} suggestionCount={SuggestionCount}",
            result.Subject,
            result.MaySearch,
            result.Suggestions.Length);

        return new AgentToolOutput(call.CallId, JsonSerializer.Serialize(result, JsonOptions));
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
    // graph_query
    // -------------------------------------------------------------------------

    public Task<AgentToolOutput> HandleGraphQueryAsync(AgentToolCall call, CancellationToken ct)
        => RunSubAgentAsync(call, _options.GraphQueryAgentName, "graph_query", ct);

    // -------------------------------------------------------------------------
    // Core sub-agent run loop
    // -------------------------------------------------------------------------

    private async Task<AgentToolOutput> RunSubAgentAsync(
        AgentToolCall call,
        string agentName,
        string toolName,
        CancellationToken ct)
    {
        var guardOutput = ValidateSearchMayRun(call);
        if (guardOutput != null)
        {
            return guardOutput;
        }

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

    private AgentToolOutput? ValidateSearchMayRun(AgentToolCall call)
    {
        if (!_questionValidationState.IsValidated)
        {
            _logger.LogWarning(
                "Search tool {ToolName} called before validate_question completed",
                call.FunctionName);

            return new AgentToolOutput(call.CallId, JsonSerializer.Serialize(new
            {
                error = "validation_required",
                message = "validate_question must be called before search tools."
            }, JsonOptions));
        }

        var result = _questionValidationState.Result;
        if (result?.MaySearch != false)
        {
            return null;
        }

        _logger.LogInformation(
            "Search tool {ToolName} blocked because query requires clarification",
            call.FunctionName);

        return new AgentToolOutput(call.CallId, JsonSerializer.Serialize(new
        {
            error = "clarification_required",
            response_type = result.ResponseType,
            clarification_question = result.ClarificationQuestion,
            suggestions = result.Suggestions
        }, JsonOptions));
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

    private static JsonElement ParseArgs(string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            return JsonDocument.Parse("{}").RootElement;
        }

        try
        {
            return JsonDocument.Parse(argumentsJson).RootElement;
        }
        catch
        {
            return JsonDocument.Parse("{}").RootElement;
        }
    }
}
