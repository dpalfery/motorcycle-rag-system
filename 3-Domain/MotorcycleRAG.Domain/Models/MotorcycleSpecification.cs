using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Core.Models;

/// <summary>
/// Represents motorcycle specification data with validation attributes
/// </summary>
public class MotorcycleSpecification
{
    [Required]
    public string Id { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string Make { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string Model { get; set; } = string.Empty;

    [Range(1900, 2030)]
    public int Year { get; set; }

    public EngineSpecification? Engine { get; set; }
    public PerformanceMetrics? Performance { get; set; }
    public SafetyFeatures? Safety { get; set; }
    public PricingInformation? Pricing { get; set; }

    /// <summary>
    /// Additional specifications stored as key-value pairs for flexibility
    /// </summary>
    public Dictionary<string, object> AdditionalSpecs { get; set; } = new();
}

/// <summary>
/// Engine specification details
/// </summary>
public class EngineSpecification
{
    [StringLength(50)]
    public string Type { get; set; } = string.Empty;

    [Range(0, 10000)]
    public int DisplacementCC { get; set; }

    [Range(0, 1000)]
    public int Horsepower { get; set; }

    [Range(0, 1000)]
    public int Torque { get; set; }

    [StringLength(50)]
    public string FuelSystem { get; set; } = string.Empty;

    [Range(0, 20)]
    public int Cylinders { get; set; }
}

/// <summary>
/// Performance metrics
/// </summary>
public class PerformanceMetrics
{
    [Range(0, 500)]
    public int TopSpeedKmh { get; set; }

    [Range(0, 20)]
    public decimal Acceleration0To100 { get; set; }

    [Range(0, 100)]
    public decimal FuelConsumptionL100km { get; set; }

    [Range(0, 1000)]
    public int RangeKm { get; set; }
}

/// <summary>
/// Safety features information
/// </summary>
public class SafetyFeatures
{
    public bool ABS { get; set; }
    public bool TractionControl { get; set; }
    public bool StabilityControl { get; set; }
    public bool AntiWheelieControl { get; set; }
    public List<string> AdditionalFeatures { get; set; } = new();
}

/// <summary>
/// Pricing information
/// </summary>
public class PricingInformation
{
    [Range(0, double.MaxValue)]
    public decimal MSRP { get; set; }

    [StringLength(10)]
    public string Currency { get; set; } = "USD";

    public DateTime PriceDate { get; set; } = DateTime.UtcNow;
    
    [StringLength(100)]
    public string Market { get; set; } = string.Empty;
}