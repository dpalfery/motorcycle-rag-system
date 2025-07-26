namespace MotorcycleRAG.Core.Models;

public class ResilienceConfiguration
{
    public CircuitBreakerConfiguration CircuitBreaker { get; set; } = new();
    public RetryConfiguration          Retry          { get; set; } = new();
    public FallbackConfiguration       Fallback       { get; set; } = new();
}