using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Contracts.Models.DTOs.Specifications;

/// <summary>
/// Motorcycle specification payload shared by ingestion and search boundaries.
/// </summary>
public class MotorcycleSpecificationDto
{
    private const int MinYear = 1900;
    private const int MaxYear = 2030;

    [Required]
    public string Id { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string Make { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string Model { get; set; } = string.Empty;

    [Range(MinYear, MaxYear)]
    public int Year { get; set; }

    public EngineSpecificationDto? Engine { get; set; }
    public PerformanceMetricsDto? Performance { get; set; }
    public SafetyFeaturesDto? Safety { get; set; }
    public PricingInformationDto? Pricing { get; set; }

    public Dictionary<string, object> AdditionalSpecs { get; } = new();
}
