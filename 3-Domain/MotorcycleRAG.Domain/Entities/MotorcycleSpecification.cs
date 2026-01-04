using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Domain.Entities;

/// <summary>
/// Represents motorcycle specification data with validation attributes
/// </summary>
public class MotorcycleSpecification
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

    public EngineSpecification? Engine { get; set; }
    public PerformanceMetrics? Performance { get; set; }
    public SafetyFeatures? Safety { get; set; }
    public PricingInformation? Pricing { get; set; }

    /// <summary>
    /// Additional specifications stored as key-value pairs for flexibility
    /// </summary>
    public Dictionary<string, object> AdditionalSpecs { get; } = new();
}

/// <summary>
/// Engine specification details
/// </summary>
public class EngineSpecification
{
    private const int MaxDisplacement = 10000;
    private const int MaxHorsepower = 1000;
    private const int MaxTorque = 1000;
    private const int MaxCylinders = 20;

    [StringLength(50)]
    public string Type { get; set; } = string.Empty;

    [Range(0, MaxDisplacement)]
    public int DisplacementCC { get; set; }

    [Range(0, MaxHorsepower)]
    public int Horsepower { get; set; }

    [Range(0, MaxTorque)]
    public int Torque { get; set; }

    [StringLength(50)]
    public string FuelSystem { get; set; } = string.Empty;

    [Range(0, MaxCylinders)]
    public int Cylinders { get; set; }
}

/// <summary>
/// Performance metrics
/// </summary>
public class PerformanceMetrics
{
    private const int MaxTopSpeed = 500;
    private const int MaxAcceleration = 20;
    private const int MaxFuelConsumption = 100;
    private const int MaxRange = 1000;

    [Range(0, MaxTopSpeed)]
    public int TopSpeedKmh { get; set; }

    [Range(0, MaxAcceleration)]
    public decimal Acceleration0To100 { get; set; }

    [Range(0, MaxFuelConsumption)]
    public decimal FuelConsumptionL100Km { get; set; }

    [Range(0, MaxRange)]
    public int RangeKm { get; set; }
}

/// <summary>
/// Safety features information
/// </summary>
public class SafetyFeatures
{
    public bool Abs { get; set; }
    public bool TractionControl { get; set; }
    public bool StabilityControl { get; set; }
    public bool AntiWheelieControl { get; set; }
    public Collection<string> AdditionalFeatures { get; } = new();
}

/// <summary>
/// Pricing information
/// </summary>
public class PricingInformation
{
    [Range(0, double.MaxValue)]
    public decimal Msrp { get; set; }

    [StringLength(10)]
    public string Currency { get; set; } = "USD";

    public DateTime PriceDate { get; set; } = DateTime.UtcNow;

    [StringLength(100)]
    public string Market { get; set; } = string.Empty;
}
