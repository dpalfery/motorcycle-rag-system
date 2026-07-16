using System.Collections.ObjectModel;

namespace MotorcycleRAG.Contracts.Models.DTOs.Specifications;

/// <summary>
/// Motorcycle safety features payload.
/// </summary>
public class SafetyFeaturesDto
{
    public bool Abs { get; set; }
    public bool TractionControl { get; set; }
    public bool StabilityControl { get; set; }
    public bool AntiWheelieControl { get; set; }
    public Collection<string> AdditionalFeatures { get; } = new();
}
