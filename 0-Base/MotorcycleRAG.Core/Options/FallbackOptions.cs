namespace MotorcycleRAG.Core.Options;

public class FallbackOptions
{
    public bool     EnableCachedResponses { get; set; } = true;
    public bool     EnableSimplifiedSearch { get; set; } = true;
    public bool     EnableOfflineMode      { get; set; } = false;
    public TimeSpan CacheExpiration       { get; set; } = TimeSpan.FromMinutes(30);
    public string   FallbackMessage       { get; set; } = "Service temporarily unavailable. Using cached or simplified results.";
}
