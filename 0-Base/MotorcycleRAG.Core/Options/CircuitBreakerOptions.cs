namespace MotorcycleRAG.Core.Options;

public class CircuitBreakerOptions
{
    public ServiceCircuitBreakerConfig OpenAI              { get; set; } = new();
    public ServiceCircuitBreakerConfig Search              { get; set; } = new();
    public ServiceCircuitBreakerConfig DocumentIntelligence{ get; set; } = new();
}
