namespace MotorcycleRAG.Core.Models;

public class CircuitBreakerConfiguration
{
    public ServiceCircuitBreakerConfig OpenAI              { get; set; } = new();
    public ServiceCircuitBreakerConfig Search              { get; set; } = new();
    public ServiceCircuitBreakerConfig DocumentIntelligence{ get; set; } = new();
}