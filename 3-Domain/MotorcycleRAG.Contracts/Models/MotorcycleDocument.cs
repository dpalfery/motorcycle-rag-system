using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations; // <-- This using is required for StringLengthAttribute

namespace MotorcycleRAG.Contracts.Models
{
    /// <summary>
    /// Represents a motorcycle document for indexing
    /// </summary>
    public class MotorcycleDocument
    {
        public string Id { get; set; } = string.Empty;
        public string Make { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public int Year { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Engine { get; set; } = string.Empty;
        public string Displacement { get; set; } = string.Empty;
        public string Power { get; set; } = string.Empty;
        public string Torque { get; set; } = string.Empty;
        public string Weight { get; set; } = string.Empty;
        public string FuelCapacity { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public Dictionary<string, object> Metadata { get; set; } = new();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string Source { get; set; } = string.Empty;

        public string Content { get; set; } = string.Empty;
        public float[] ContentVector { get; set; } = Array.Empty<float>();
    }

    /// <summary>
    /// Detailed motorcycle specification
    /// </summary>
    public class MotorcycleSpecification
    {
        public string Id { get; set; } = string.Empty;
        public string Make { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public int Year { get; set; }
        public string Category { get; set; } = string.Empty;
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

        [Range(0, 100000)]
        public int DisplacementCC { get; set; }

        [Range(0, 10000)]
        public int Horsepower { get; set; }

        [Range(0, 1000)]
        public int Torque { get; set; }

        [StringLength(50)]
        public string FuelSystem { get; set; } = string.Empty;

        [Range(0, 12)]
        public int Cylinders { get; set; }
    }

    /// <summary>
    /// Performance metrics
    /// </summary>
    public class PerformanceMetrics
    {
        [System.ComponentModel.DataAnnotations.Range(0, 500)]
        public int TopSpeedKmh { get; set; }

        [System.ComponentModel.DataAnnotations.Range(0, 20)]
        public decimal Acceleration0To100 { get; set; }

        [System.ComponentModel.DataAnnotations.Range(0, 10)]
        public decimal FuelConsumptionL100km { get; set; }

        [System.ComponentModel.DataAnnotations.Range(0, 1000)]
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
}
