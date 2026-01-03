using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Core.Options;

public class TrustedSourceOptions
{
    private const float DefaultCredibilityScore = 0.5f;

    [Required] public string Name             { get; set; } = string.Empty;
    [Required, Url] public Uri BaseUrl      { get; set; } = new Uri("https://example.com", UriKind.Absolute);
    [Required] public Uri SearchUrlTemplate{ get; set; } = new Uri("https://example.com", UriKind.Absolute);
    [Required] public string ContentSelector   { get; set; } = string.Empty;
    [Range(0, 1)] public float CredibilityScore { get; set; } = DefaultCredibilityScore;
}
