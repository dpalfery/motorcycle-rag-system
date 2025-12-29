
namespace MotorcycleRAG.Core.Options;

public class CircuitBreakerOptions
{
    public ServiceCircuitBreakerOptions OpenAI              { get; set; } = new();
    public ServiceCircuitBreakerOptions Search              { get; set; } = new();
    public ServiceCircuitBreakerOptions DocumentIntelligence{ get; set; } = new();
}
