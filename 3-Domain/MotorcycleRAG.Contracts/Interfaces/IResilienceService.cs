namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Interface for resilience service operations
/// </summary>
public interface IResilienceService
{
    /// <summary>
    /// Executes an operation with retry policy
    /// </summary>
    Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation, string operationName);

    /// <summary>
    /// Executes an operation with circuit breaker
    /// </summary>
    Task<T> ExecuteWithCircuitBreakerAsync<T>(Func<Task<T>> operation, string operationName);

    /// <summary>
    /// Executes an operation with timeout
    /// </summary>
    Task<T> ExecuteWithTimeoutAsync<T>(Func<Task<T>> operation, TimeSpan timeout, string operationName);

    // New unified ExecuteAsync with optional fallback + correlation + cancellation
    Task<T> ExecuteAsync<T>(
        string policyKey,
        Func<Task<T>> operation,
        Func<Task<T>>? fallback = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default);

    Task ExecuteAsync(
        string policyKey,
        Func<Task> operation,
        Func<Task>? fallback = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default);
}
