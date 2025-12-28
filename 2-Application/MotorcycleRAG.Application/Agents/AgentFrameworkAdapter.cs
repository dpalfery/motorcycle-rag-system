using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models;
using MotorcycleRAG.Domain.Models;
using MotorcycleRAG.Contracts.Options;

namespace MotorcycleRAG.Application.Agents;

/// <summary>
/// Adapter that bridges between custom search agent interfaces and Microsoft Agent Framework.
/// Provides tool execution and agent communication abstractions.
/// </summary>
public class AgentFrameworkAdapter
{
    private readonly ILogger _logger;
    private readonly Dictionary<string, Func<ToolCall, Task<ToolExecutionResult>>> _toolHandlers;

    public AgentFrameworkAdapter(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _toolHandlers = new Dictionary<string, Func<ToolCall, Task<ToolExecutionResult>>>();
    }

    /// <summary>
    /// Register a tool handler for a specific tool name
    /// </summary>
    public void RegisterToolHandler(string toolName, Func<ToolCall, Task<ToolExecutionResult>> handler)
    {
        if (string.IsNullOrWhiteSpace(toolName))
            throw new ArgumentException("Tool name cannot be empty", nameof(toolName));

        if (handler == null)
            throw new ArgumentNullException(nameof(handler));

        _toolHandlers[toolName] = handler;
        _logger.LogInformation("Registered tool handler for: {ToolName}", toolName);
    }

    /// <summary>
    /// Execute a tool call
    /// </summary>
    public async Task<ToolExecutionResult> ExecuteToolAsync(ToolCall toolCall)
    {
        if (toolCall == null)
            throw new ArgumentNullException(nameof(toolCall));

        if (string.IsNullOrWhiteSpace(toolCall.ToolName))
        {
            return new ToolExecutionResult
            {
                ToolCallId = toolCall.CallId,
                Success = false,
                Error = "Tool name is required"
            };
        }

        if (!_toolHandlers.TryGetValue(toolCall.ToolName, out var handler))
        {
            _logger.LogWarning("No handler registered for tool: {ToolName}", toolCall.ToolName);
            return new ToolExecutionResult
            {
                ToolCallId = toolCall.CallId,
                Success = false,
                Error = $"No handler registered for tool: {toolCall.ToolName}"
            };
        }

        try
        {
            _logger.LogInformation("Executing tool: {ToolName} (CallId: {CallId})", toolCall.ToolName, toolCall.CallId);
            var result = await handler(toolCall);
            _logger.LogInformation("Tool execution completed: {ToolName}, Success: {Success}", toolCall.ToolName, result.Success);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool execution failed: {ToolName}", toolCall.ToolName);
            return new ToolExecutionResult
            {
                ToolCallId = toolCall.CallId,
                Success = false,
                Error = $"Tool execution failed: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Create a tool execution handler for a search agent
    /// </summary>
    public static Func<ToolCall, Task<ToolExecutionResult>> CreateSearchAgentHandler(
        ISearchAgent agent,
        ILogger logger)
    {
        return async toolCall =>
        {
            try
            {
                var query = toolCall.GetStringArgument("query");
                var maxResults = toolCall.GetIntArgument("maxResults", 10);
                var scoreArg = toolCall.GetArgument<double>("minRelevanceScore");
                var minRelevanceScore = scoreArg > 0 ? (float)scoreArg : 0.5f;

                if (string.IsNullOrWhiteSpace(query))
                {
                    return new ToolExecutionResult
                    {
                        ToolCallId = toolCall.CallId,
                        Success = false,
                        Error = "Query is required"
                    };
                }

                var searchParameters = new SearchParameters {
                    MaxResults = Math.Min(maxResults, 50),
                    MinRelevanceScore = minRelevanceScore,
                    EnableCaching = true,
                    IncludeMetadata = true
                };

                var results = await agent.SearchAsync(query, searchParameters);

                return new ToolExecutionResult
                {
                    ToolCallId = toolCall.CallId,
                    Success = true,
                    Result = new
                    {
                        results = results,
                        resultCount = results.Length,
                        agentType = agent.AgentType.ToString(),
                        executedAt = DateTime.UtcNow
                    }
                };
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Search agent handler failed for tool call: {CallId}", toolCall.CallId);
                return new ToolExecutionResult
                {
                    ToolCallId = toolCall.CallId,
                    Success = false,
                    Error = ex.Message
                };
            }
        };
    }

    /// <summary>
    /// Create a tool execution handler for the orchestrator
    /// </summary>
    public static Func<ToolCall, Task<ToolExecutionResult>> CreateOrchestratorHandler(
        IAgentOrchestrator orchestrator,
        ILogger logger)
    {
        return async toolCall =>
        {
            try
            {
                var query = toolCall.GetStringArgument("query");
                var maxResults = toolCall.GetIntArgument("maxResults", 10);
                var scoreArg = toolCall.GetArgument<double>("minRelevanceScore");
                var minRelevanceScore = scoreArg > 0 ? (float)scoreArg : 0.5f;

                if (string.IsNullOrWhiteSpace(query))
                {
                    return new ToolExecutionResult
                    {
                        ToolCallId = toolCall.CallId,
                        Success = false,
                        Error = "Query is required"
                    };
                }

                var searchParameters = new SearchParameters {
                    MaxResults = Math.Min(maxResults, 50),
                    MinRelevanceScore = minRelevanceScore,
                    EnableCaching = true,
                    IncludeMetadata = true
                };

                var results = await orchestrator.OrchestrateSearchAsync(query, searchParameters);

                return new ToolExecutionResult
                {
                    ToolCallId = toolCall.CallId,
                    Success = true,
                    Result = new
                    {
                        results = results,
                        resultCount = results.Length,
                        executedAt = DateTime.UtcNow
                    }
                };
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Orchestrator handler failed for tool call: {CallId}", toolCall.CallId);
                return new ToolExecutionResult
                {
                    ToolCallId = toolCall.CallId,
                    Success = false,
                    Error = ex.Message
                };
            }
        };
    }

    /// <summary>
    /// Get registered tool names
    /// </summary>
    public IReadOnlyCollection<string> GetRegisteredTools() => _toolHandlers.Keys.ToList().AsReadOnly();

    /// <summary>
    /// Check if a tool is registered
    /// </summary>
    public bool IsToolRegistered(string toolName) => _toolHandlers.ContainsKey(toolName);
}

/// <summary>
/// Agent communication context for inter-agent messaging
/// </summary>
public class AgentCommunicationContext
{
    public string SenderId { get; set; } = string.Empty;
    public string ReceiverId { get; set; } = string.Empty;
    public string MessageType { get; set; } = string.Empty;
    public Dictionary<string, object> Payload { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string ConversationId { get; set; } = Guid.NewGuid().ToString();

    public T? GetPayloadValue<T>(string key)
    {
        if (!Payload.TryGetValue(key, out var value))
            return default;

        return (T?)Convert.ChangeType(value, typeof(T));
    }
}

/// <summary>
/// Factory for creating agent framework adapters
/// </summary>
public class AgentFrameworkAdapterFactory
{
    private readonly ILogger _logger;

    public AgentFrameworkAdapterFactory(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Create an adapter with standard tool handlers
    /// </summary>
    public AgentFrameworkAdapter CreateStandardAdapter(
        IEnumerable<ISearchAgent> searchAgents,
        IAgentOrchestrator? orchestrator,
        ILogger logger)
    {
        var adapter = new AgentFrameworkAdapter(_logger);

        foreach (var agent in searchAgents)
        {
            var toolName = agent.AgentType switch
            {
                SearchAgentType.VectorSearch => "vector_search",
                SearchAgentType.WebSearch => "web_search",
                SearchAgentType.PDFSearch => "pdf_search",
                _ => $"agent_{agent.AgentType.ToString().ToLower()}"
            };

            var handler = AgentFrameworkAdapter.CreateSearchAgentHandler(agent, logger);
            adapter.RegisterToolHandler(toolName, handler);
        }

        if (orchestrator != null)
        {
            var orchestratorHandler = AgentFrameworkAdapter.CreateOrchestratorHandler(orchestrator, logger);
            adapter.RegisterToolHandler("plan_search_strategy", orchestratorHandler);
        }

        return adapter;
    }
}
