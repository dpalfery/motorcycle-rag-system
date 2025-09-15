namespace MotorcycleRAG.Domain.Models;

/// <summary>
/// Circuit breaker state enumeration
/// </summary>
public enum CircuitBreakerState
{
    Closed,
    Open,
    HalfOpen
}
