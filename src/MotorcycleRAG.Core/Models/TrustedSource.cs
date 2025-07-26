using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Core.Models;

public class TrustedSource
{
    [Required] public string Name             { get; set; } = string.Empty;
    [Required, Url] public string BaseUrl      { get; set; } = string.Empty;
    [Required] public string SearchUrlTemplate{ get; set; } = string.Empty;
    [Required] public string ContentSelector   { get; set; } = string.Empty;
    [Range(0,1)] public float CredibilityScore { get; set; } = 0.5f;
}