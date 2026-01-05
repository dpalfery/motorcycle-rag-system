using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Core.Options;

public class WebSearchOptions {
    private const int DefaultMaxConcurrentRequests = 3;
    private const int DefaultMinRequestIntervalMs = 1000;
    private const int DefaultRequestTimeoutSeconds = 30;
    private const float DefaultMinCredibilityScore = 0.6f;
    private const int MaxConcurrentRequestsLimit = 10;
    private const int MinRequestIntervalMsMin = 100;
    private const int MinRequestIntervalMsMax = 10_000;
    private const int RequestTimeoutSecondsMin = 5;
    private const int RequestTimeoutSecondsMax = 120;

    [Range(1, MaxConcurrentRequestsLimit)] public int MaxConcurrentRequests { get; set; } = DefaultMaxConcurrentRequests;
    [Range(MinRequestIntervalMsMin, MinRequestIntervalMsMax)] public int MinRequestIntervalMs { get; set; } = DefaultMinRequestIntervalMs;
    [Range(RequestTimeoutSecondsMin, RequestTimeoutSecondsMax)] public int RequestTimeoutSeconds { get; set; } = DefaultRequestTimeoutSeconds;
    [Range(0, 1)] public float MinCredibilityScore { get; set; } = DefaultMinCredibilityScore;

    [Required] public string SearchTermModel { get; set; } = "gpt-4o-mini";
    [Required] public string ValidationModel { get; set; } = "gpt-4o-mini";

    public Collection<TrustedSourceOptions> TrustedSources { get; } = new();
}
