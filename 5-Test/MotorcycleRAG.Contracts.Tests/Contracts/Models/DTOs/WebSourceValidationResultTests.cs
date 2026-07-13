using MotorcycleRAG.Contracts.Models.DTOs.Web;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.UnitTests.Contracts.Models.DTOs;

public sealed class WebSourceValidationResultTests
{
    // ---- QualityMultiplier: computed as Math.Max(0.5f, QualityScore) ----

    [Theory]
    [InlineData(0.8f, 0.8f)]   // above the 0.5 floor -> returned unchanged
    [InlineData(0.3f, 0.5f)]   // below the floor -> clamped up to 0.5
    [InlineData(0.5f, 0.5f)]   // exactly at the floor boundary
    [InlineData(1.0f, 1.0f)]   // high-quality upper bound
    public void QualityMultiplier_ClampsScoreToMinimumFloor(float qualityScore, float expected)
    {
        var result = new WebSourceValidationResult { QualityScore = qualityScore };

        result.QualityMultiplier.Should().Be(expected);
    }

    // ---- GetTierMultiplier switch (private; observed via Approved().TierMultiplier) ----

    private static ContentValidation ValidContent(float qualityScore) =>
        new() { IsValid = true, QualityScore = qualityScore };

    [Theory]
    [InlineData(WebTrustTier.TierA, 1.5f)]
    [InlineData(WebTrustTier.TierB, 1.1f)]
    [InlineData(WebTrustTier.TierC, 0.9f)]
    [InlineData(WebTrustTier.None, 1.0f)] // None is not a switch case -> default arm
    public void GetTierMultiplier_MapsEachTierToItsMultiplier(WebTrustTier tier, float expected)
    {
        var result = WebSourceValidationResult.Approved(ValidContent(0.9f), tier);

        result.TierMultiplier.Should().Be(expected);
        result.Tier.Should().Be(tier);
    }

    [Fact]
    public void GetTierMultiplier_UndefinedTierFallsThroughToDefaultArm()
    {
        // An out-of-range enum value must hit the switch default arm and yield 1.0.
        var undefinedTier = (WebTrustTier)999;

        var result = WebSourceValidationResult.Approved(ValidContent(0.9f), undefinedTier);

        result.TierMultiplier.Should().Be(1.0f);
    }

    // ---- Approved factory ----

    [Fact]
    public void Approved_WithValidContent_BuildsApprovedResult()
    {
        var content = ValidContent(0.9f);

        var result = WebSourceValidationResult.Approved(content, WebTrustTier.TierA);

        result.IsValid.Should().BeTrue();
        result.QualityScore.Should().Be(0.9f);
        result.Tier.Should().Be(WebTrustTier.TierA);
        result.TierMultiplier.Should().Be(1.5f);
        result.RejectionReason.Should().BeNull();
    }

    [Fact]
    public void Approved_WithNullContent_ThrowsArgumentNullException()
    {
        Action act = () => WebSourceValidationResult.Approved(null!, WebTrustTier.TierA);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("contentValidation");
    }

    // ---- Rejected factory ----

    [Fact]
    public void Rejected_MarksInvalidAndRecordsReason()
    {
        var result = WebSourceValidationResult.Rejected("unsupported source");

        result.IsValid.Should().BeFalse();
        result.RejectionReason.Should().Be("unsupported source");
    }

    // ---- ApprovedWithError factory ----

    [Fact]
    public void ApprovedWithError_ReturnsApprovedResultWithFixedQualityShape()
    {
        var result = WebSourceValidationResult.ApprovedWithError("partial failure");

        result.IsValid.Should().BeTrue();
        result.QualityScore.Should().Be(0.7f);
        result.Tier.Should().Be(WebTrustTier.None);
        result.TierMultiplier.Should().Be(1.0f);
        result.RejectionReason.Should().BeNull();
    }
}
