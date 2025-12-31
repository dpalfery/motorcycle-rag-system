namespace MotorcycleRAG.Core.Options;

public class ResilienceOptions
{
    public CircuitBreakerOptions CircuitBreaker { get; set; } = new();
    public RetryOptions          Retry          { get; set; } = new();
    public FallbackOptions       Fallback       { get; set; } = new();
}
