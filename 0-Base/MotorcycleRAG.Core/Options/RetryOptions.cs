using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Core.Options;

public class RetryOptions
{
    private const int DefaultMaxRetries = 3;
    private const int DefaultBaseDelaySeconds = 2;
    private const int DefaultMaxDelaySeconds = 60;
    private const int MaxRetriesLimit = 10;
    private const int BaseDelaySecondsLimit = 300;
    private const int MaxDelaySecondsLimit = 600;

    [Range(1, MaxRetriesLimit)]  public int  MaxRetries        { get; set; } = DefaultMaxRetries;
    [Range(1, BaseDelaySecondsLimit)] public int  BaseDelaySeconds  { get; set; } = DefaultBaseDelaySeconds;
    [Range(1, MaxDelaySecondsLimit)] public int  MaxDelaySeconds   { get; set; } = DefaultMaxDelaySeconds;
    public bool UseExponentialBackoff { get; set; } = true;
}
