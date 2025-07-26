using MotorcycleRAG.Core.Models;

namespace MotorcycleRAG.Core.Interfaces;

/// <summary>
/// Interface for resilience service providing circuit breaker, retry, and fallback mechanisms
/// </summary>
public interface IResilienceService
{
    /// <summary>
    /// Executes an operation with resilience policies applied
    /// </summary>
    Task<T> ExecuteAsync<T>(
        string policyKey,
        Func<Task<T>> operation,
        Func<Task<T>>? fallback = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes an operation without return value with resilience policies applied
    /// </summary>
    Task ExecuteAsync(
        string policyKey,
        Func<Task> operation,
        Func<Task>? fallback = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current state of a circuit breaker
    /// </summary>
    CircuitBreakerState GetCircuitBreakerState(string policyKey);

    /// <summary>
    /// Gets health status of all circuit breakers
    /// </summary>
    Dictionary<string, CircuitBreakerState> GetHealthStatus();
}