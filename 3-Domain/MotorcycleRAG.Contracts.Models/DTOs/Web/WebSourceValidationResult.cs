using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.Contracts.Models.DTOs.Web;

public class WebSourceValidationResult {
    public bool IsValid { get; init; }
    public string? RejectionReason { get; init; }
    public float QualityScore { get; init; }
    public float QualityMultiplier => Math.Max(0.5f, QualityScore);
    public WebTrustTier Tier { get; init; }
    public float TierMultiplier { get; init; }

    public static WebSourceValidationResult Approved(ContentValidation contentValidation, WebTrustTier tier) {
        ArgumentNullException.ThrowIfNull(contentValidation);

        return new WebSourceValidationResult {
            IsValid = true,
            QualityScore = contentValidation.QualityScore,
            Tier = tier,
            TierMultiplier = GetTierMultiplier(tier)
        };
    }

    public static WebSourceValidationResult Rejected(string reason) {
        return new WebSourceValidationResult { IsValid = false, RejectionReason = reason };
    }

    public static WebSourceValidationResult ApprovedWithError(string error) {
        return new WebSourceValidationResult {
            IsValid = true,
            QualityScore = 0.7f,
            Tier = WebTrustTier.None,
            TierMultiplier = 1.0f
        };
    }

    private static float GetTierMultiplier(WebTrustTier tier) {
        return tier switch {
            WebTrustTier.TierA => 1.5f,
            WebTrustTier.TierB => 1.1f,
            WebTrustTier.TierC => 0.9f,
            _ => 1.0f
        };
    }
}
