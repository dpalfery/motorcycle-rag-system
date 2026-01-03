using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Core.Options;

public class ServiceCircuitBreakerOptions
{
    private const int DefaultFailureThreshold = 5;
    private const int DefaultMinimumThroughput = 10;
    private const int FailureThresholdLimit = 20;
    private const int MinimumThroughputLimit = 100;

    [Range(1, FailureThresholdLimit)]
    public int FailureThreshold { get; set; } = DefaultFailureThreshold;
    public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromMinutes(1);
    [Range(1, MinimumThroughputLimit)]
    public int MinimumThroughput { get; set; } = DefaultMinimumThroughput;
}
