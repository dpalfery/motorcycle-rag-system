using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Contracts.Models.DTOs.Specifications;

/// <summary>
/// Motorcycle pricing payload.
/// </summary>
public class PricingInformationDto
{
    [Range(0, double.MaxValue)]
    public decimal Msrp { get; set; }

    [StringLength(10)]
    public string Currency { get; set; } = "USD";

    public DateTime PriceDate { get; set; } = DateTime.UtcNow;

    [StringLength(100)]
    public string Market { get; set; } = string.Empty;
}
