using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Core.Models;

public class ServiceCircuitBreakerConfig
{
    [Range(1,20)]  public int FailureThreshold { get; set; } = 5;
    public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromMinutes(1);
    [Range(1,100)] public int MinimumThroughput { get; set; } = 10;
}