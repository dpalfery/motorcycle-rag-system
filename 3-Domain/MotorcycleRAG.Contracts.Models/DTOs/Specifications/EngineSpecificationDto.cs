using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Contracts.Models.DTOs.Specifications;

/// <summary>
/// Engine specification payload.
/// </summary>
public class EngineSpecificationDto
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
