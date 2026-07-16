using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Contracts.Models.DTOs.Specifications;

/// <summary>
/// Motorcycle performance metrics payload.
/// </summary>
public class PerformanceMetricsDto
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
