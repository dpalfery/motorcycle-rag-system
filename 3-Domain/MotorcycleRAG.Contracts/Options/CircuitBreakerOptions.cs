using MotorcycleRAG.Domain.Models;

namespace MotorcycleRAG.Contracts.Options;

public class CircuitBreakerOptions
{
    public ServiceCircuitBreakerConfig OpenAI              { get; set; } = new();
    public ServiceCircuitBreakerConfig Search              { get; set; } = new();
    public ServiceCircuitBreakerConfig DocumentIntelligence{ get; set; } = new();
}
