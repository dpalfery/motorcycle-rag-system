namespace MotorcycleRAG.Core.Interfaces;

/// <summary>
/// Interface for correlation service managing correlation IDs throughout the request lifecycle
/// </summary>
public interface ICorrelationService
{
    /// <summary>
    /// Gets the current correlation ID or generates a new one
    /// </summary>
    string GetOrCreateCorrelationId();

    /// <summary>
    /// Sets the correlation ID for the current context
    /// </summary>
    void SetCorrelationId(string correlationId);

    /// <summary>
    /// Clears the current correlation ID
    /// </summary>
    void ClearCorrelationId();

    /// <summary>
    /// Executes an operation with a specific correlation ID
    /// </summary>
    Task<T> ExecuteWithCorrelationAsync<T>(string correlationId, Func<Task<T>> operation);

    /// <summary>
    /// Executes an operation with a specific correlation ID (no return value)
    /// </summary>
    Task ExecuteWithCorrelationAsync(string correlationId, Func<Task> operation);

    /// <summary>
    /// Creates a logging scope with the current correlation ID
    /// </summary>
    IDisposable CreateLoggingScope();

    /// <summary>
    /// Creates a logging scope with additional properties
    /// </summary>
    IDisposable CreateLoggingScope(Dictionary<string, object> additionalProperties);
}