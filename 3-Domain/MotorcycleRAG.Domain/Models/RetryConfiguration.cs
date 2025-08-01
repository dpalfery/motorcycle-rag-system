using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Core.Models;

public class RetryConfiguration
{
    [Range(1,10)]  public int  MaxRetries        { get; set; } = 3;
    [Range(1,300)] public int  BaseDelaySeconds  { get; set; } = 2;
    [Range(1,600)] public int  MaxDelaySeconds   { get; set; } = 60;
    public bool UseExponentialBackoff { get; set; } = true;
}