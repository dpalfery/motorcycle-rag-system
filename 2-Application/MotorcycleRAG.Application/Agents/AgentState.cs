using System.Collections.ObjectModel;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.Application.Agents;

/// <summary>
/// Manages the execution state and context for agents in the Microsoft Agent Framework.
/// </summary>
public class AgentState {
    /// <summary>
    /// Unique identifier for this execution context
    /// </summary>
    public string ExecutionId { get; } = Guid.NewGuid().ToString();

    /// <summary>
    /// The original user query
    /// </summary>
    public string OriginalQuery { get; set; } = string.Empty;

    /// <summary>
    /// Accumulated search results from all agents
    /// </summary>
    public Collection<SearchResult> AccumulatedResults { get; } = new();

    /// <summary>
    /// Current search context
    /// </summary>
    public SearchContext? SearchContext { get; set; }

    /// <summary>
    /// Search options for the current query
    /// </summary>
    public SearchOptions? SearchOptions { get; set; }

    /// <summary>
    /// Execution status
    /// </summary>
    public AgentExecutionStatus Status { get; set; } = AgentExecutionStatus.Pending;

    /// <summary>
    /// Messages exchanged between agents
    /// </summary>
    public Collection<AgentMessage> Messages { get; } = new();

    /// <summary>
    /// Execution metadata
    /// </summary>
    public Dictionary<string, object> Metadata { get; } = new();

    /// <summary>
    /// Timestamp when execution started
    /// </summary>
    public DateTime StartTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when execution completed (if completed)
    /// </summary>
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// Any errors encountered during execution
    /// </summary>
    public Collection<AgentError> Errors { get; } = new();

    /// <summary>
    /// Get total execution duration
    /// </summary>
    public TimeSpan ExecutionDuration => (EndTime ?? DateTime.UtcNow) - StartTime;

    /// <summary>
    /// Add a message to the conversation
    /// </summary>
    public void AddMessage(string sender, string content, AgentMessageType type = AgentMessageType.Status) {
        Messages.Add(new AgentMessage {
            SenderId = sender,
            Content = content,
            Type = type,
            Timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Record an error during execution
    /// </summary>
    public void RecordError(string agentId, string errorMessage, Exception? exception = null) {
        Errors.Add(new AgentError {
            AgentId = agentId,
            Message = errorMessage,
            Exception = exception,
            Timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Mark execution as complete
    /// </summary>
    public void MarkComplete() {
        Status = AgentExecutionStatus.Completed;
        EndTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Mark execution as failed
    /// </summary>
    public void MarkFailed() {
        Status = AgentExecutionStatus.Failed;
        EndTime = DateTime.UtcNow;
    }
}

/// <summary>
/// Represents a message exchanged between agents
/// </summary>
public class AgentMessage {
    public string SenderId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public AgentMessageType Type { get; set; } = AgentMessageType.Status;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> Metadata { get; } = new();
}

/// <summary>
/// Represents an error during agent execution
/// </summary>
public class AgentError {
    public string AgentId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Exception? Exception { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Types of messages agents can send
/// </summary>
public enum AgentMessageType {
    Status,
    SearchQuery,
    SearchResult,
    Error,
    FunctionCall,
    FunctionReturn
}

/// <summary>
/// Execution status of an agent
/// </summary>
public enum AgentExecutionStatus {
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}
