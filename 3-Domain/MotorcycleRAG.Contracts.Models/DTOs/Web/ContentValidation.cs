namespace MotorcycleRAG.Contracts.Models.DTOs.Web;

public class ContentValidation {
    public bool IsValid { get; set; }
    public float QualityScore { get; set; }
    public string Reasoning { get; set; } = string.Empty;
}
