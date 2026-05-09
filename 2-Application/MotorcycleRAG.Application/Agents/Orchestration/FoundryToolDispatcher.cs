using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Application.Agents.Orchestration;

/// <summary>
/// Maps Foundry tool names to async handler delegates and dispatches tool calls.
/// Exceptions in individual handlers are caught and converted to error tool outputs —
/// the run loop is never interrupted by a single failing handler.
/// </summary>
public sealed class FoundryToolDispatcher
{
    private readonly ILogger<FoundryToolDispatcher> _logger;

    private readonly Dictionary<string, Func<AgentToolCall, CancellationToken, Task<AgentToolOutput>>> _handlers
        = new(StringComparer.OrdinalIgnoreCase);

    public FoundryToolDispatcher(ILogger<FoundryToolDispatcher> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>
    /// Registers a handler for a tool name. Overwrites any existing handler for the same name.
    /// </summary>
    public void RegisterHandler(
        string toolName,
        Func<AgentToolCall, CancellationToken, Task<AgentToolOutput>> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[toolName] = handler;
        _logger.LogDebug("Registered handler for tool '{ToolName}'", toolName);
    }

    /// <summary>
    /// Dispatches all <paramref name="calls"/> to their registered handlers.
    /// Unregistered tools and handler exceptions produce error outputs rather than throwing.
    /// </summary>
    public async Task<IReadOnlyList<AgentToolOutput>> DispatchAsync(
        IEnumerable<AgentToolCall> calls,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(calls);

        var tasks = calls.Select(call => DispatchSingleAsync(call, ct));
        var outputs = await Task.WhenAll(tasks);

        return Array.AsReadOnly(outputs);
    }

    private async Task<AgentToolOutput> DispatchSingleAsync(AgentToolCall call, CancellationToken ct)
    {
        _logger.LogDebug("Dispatching tool call '{ToolName}' (callId={CallId})", call.FunctionName, call.CallId);

        if (!_handlers.TryGetValue(call.FunctionName, out var handler))
        {
            _logger.LogWarning("No handler registered for tool '{ToolName}' (callId={CallId})", call.FunctionName, call.CallId);
            return new AgentToolOutput(call.CallId, $"{{\"error\":\"Tool '{call.FunctionName}' is not registered\"}}");
        }

        try
        {
            var output = await handler(call, ct);
            _logger.LogDebug(
                "Tool '{ToolName}' (callId={CallId}) completed ({OutputLength} chars)",
                call.FunctionName, call.CallId, output.Output.Length);
            return output;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Handler for tool '{ToolName}' (callId={CallId}) threw an exception",
                call.FunctionName, call.CallId);
            return new AgentToolOutput(call.CallId,
                $"{{\"error\":\"Tool '{call.FunctionName}' failed: {System.Text.Json.JsonEncodedText.Encode(ex.Message)}\"}}");
        }
    }
}
