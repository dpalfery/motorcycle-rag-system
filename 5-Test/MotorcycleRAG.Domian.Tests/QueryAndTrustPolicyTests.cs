using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.Domian.Tests.Domain;

public sealed class QueryAndTrustPolicyTests
{
    [Fact]
    public void WebTrustPolicy_WhenConfigured_RetainsAllowlistAndBlockingDecision()
    {
        // Arrange
        var createdAt = DateTime.Parse("2026-07-12T12:00:00Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        var policyId = Guid.NewGuid();

        // Act
        var policy = WebTrustPolicy.Rehydrate(
            policyId,
            "*.honda.com",
            WebTrustTier.TierA,
            isBlocked: false,
            reason: "Manufacturer technical documentation.",
            allowSubdomains: true,
            createdAt: createdAt,
            updatedAt: null);

        // Assert
        policy.Should().BeEquivalentTo(new
        {
            Id = policyId,
            DomainPattern = "*.honda.com",
            Tier = WebTrustTier.TierA,
            IsBlocked = false,
            Reason = "Manufacturer technical documentation.",
            AllowSubdomains = true,
            CreatedAt = createdAt,
            UpdatedAt = (DateTime?)null
        });
    }

    [Fact]
    public void WebTrustPolicy_MatchesNormalizedSubdomainsAndHonorsBlockingTransition()
    {
        var policy = WebTrustPolicy.Create("*.Honda.com", WebTrustTier.TierA, allowSubdomains: true);

        policy.MatchesDomain("DOCS.HONDA.COM").Should().BeTrue();
        policy.MatchesDomain("other.example.com").Should().BeFalse();

        policy.Block("robots policy");

        policy.IsBlocked.Should().BeTrue();
        policy.Reason.Should().Be("robots policy");

        policy.Allow("reviewed");

        policy.IsBlocked.Should().BeFalse();
        policy.Reason.Should().Be("reviewed");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  .  ")]
    public void Create_WhenDomainPatternIsEmpty_ThrowsArgumentException(string domainPattern)
    {
        // Act
        var act = () => WebTrustPolicy.Create(domainPattern, WebTrustTier.TierA);

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName(nameof(domainPattern));
    }

    [Fact]
    public void Create_WhenNonBlockedPolicyHasNoTier_ThrowsArgumentException()
    {
        // Act
        var act = () => WebTrustPolicy.Create("honda.com", WebTrustTier.None);

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("tier");
    }

    [Fact]
    public void Create_WhenBlockedPolicyHasNoTier_CreatesNormalizedPolicy()
    {
        // Act
        var policy = WebTrustPolicy.Create(" Honda.com. ", WebTrustTier.None, reason: "  robots  ", isBlocked: true);

        // Assert
        policy.Id.Should().NotBeEmpty();
        policy.DomainPattern.Should().Be("honda.com");
        policy.Tier.Should().Be(WebTrustTier.None);
        policy.IsBlocked.Should().BeTrue();
        policy.Reason.Should().Be("robots");
    }

    [Theory]
    [InlineData("honda.com", "HONDA.COM", true)]
    [InlineData("honda.com", "docs.honda.com", false)]
    [InlineData("*.honda.com", "honda.com", true)]
    [InlineData("*.honda.com", "docs.honda.com", false)]
    [InlineData("*.honda.com", "docs.honda.com", true)]
    public void MatchesDomain_WhenPolicyPatternVaries_ReturnsExpectedMatch(
        string domainPattern,
        string domain,
        bool expectedMatch)
    {
        // Arrange
        var policy = WebTrustPolicy.Create(
            domainPattern,
            WebTrustTier.TierA,
            allowSubdomains: domainPattern.StartsWith("*.", StringComparison.Ordinal) && expectedMatch);

        // Act
        var matches = policy.MatchesDomain(domain);

        // Assert
        matches.Should().Be(expectedMatch);
    }

    [Fact]
    public void MatchesDomain_WhenDomainIsEmpty_ReturnsFalse()
    {
        // Arrange
        var configuredPolicy = WebTrustPolicy.Create("honda.com", WebTrustTier.TierA);

        // Act
        var emptyDomainMatches = configuredPolicy.MatchesDomain(" ");

        // Assert
        emptyDomainMatches.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Block_WhenReasonIsNullOrWhitespace_ThrowsArgumentException(string? reason)
    {
        // Arrange
        var policy = WebTrustPolicy.Create("honda.com", WebTrustTier.TierA);

        // Act
        var act = () => policy.Block(reason!);

        // Assert
        act.Should().Throw<ArgumentException>();
        policy.IsBlocked.Should().BeFalse();
    }

    [Fact]
    public void Block_WhenReasonIsValid_NormalizesReasonAndRecordsUpdate()
    {
        // Arrange
        var policy = WebTrustPolicy.Create("honda.com", WebTrustTier.TierA);
        var beforeBlock = DateTime.UtcNow;

        // Act
        policy.Block("  robots policy  ");

        // Assert
        policy.IsBlocked.Should().BeTrue();
        policy.Reason.Should().Be("robots policy");
        policy.UpdatedAt.Should().NotBeNull();
        policy.UpdatedAt!.Value.Should().BeOnOrAfter(beforeBlock);
    }

    [Fact]
    public void Allow_WhenReasonIsOmitted_ClearsBlockAndReason()
    {
        // Arrange
        var policy = WebTrustPolicy.Create("honda.com", WebTrustTier.TierA, reason: "blocked", isBlocked: true);

        // Act
        policy.Allow();

        // Assert
        policy.IsBlocked.Should().BeFalse();
        policy.Reason.Should().BeNull();
        policy.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void ChangeTier_WhenReasonIsProvided_UpdatesTierAndNormalizedReason()
    {
        // Arrange
        var policy = WebTrustPolicy.Create("honda.com", WebTrustTier.TierA);

        // Act
        policy.ChangeTier(WebTrustTier.TierB, "  reviewed  ");

        // Assert
        policy.Tier.Should().Be(WebTrustTier.TierB);
        policy.Reason.Should().Be("reviewed");
        policy.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void ChangeTier_WhenReasonIsOmitted_RetainsReason()
    {
        // Arrange
        var policy = WebTrustPolicy.Create("honda.com", WebTrustTier.TierA, reason: "baseline");

        // Act
        policy.ChangeTier(WebTrustTier.TierC);

        // Assert
        policy.Tier.Should().Be(WebTrustTier.TierC);
        policy.Reason.Should().Be("baseline");
    }

    [Fact]
    public void ChangeTier_WhenNoneIsRequested_RejectsAllowedPolicyAndPermitsBlockedPolicy()
    {
        // Arrange
        var allowedPolicy = WebTrustPolicy.Create("honda.com", WebTrustTier.TierA);
        var blockedPolicy = WebTrustPolicy.Create("honda.com", WebTrustTier.TierA, isBlocked: true);

        // Act
        var act = () => allowedPolicy.ChangeTier(WebTrustTier.None);
        blockedPolicy.ChangeTier(WebTrustTier.None);

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("tier");
        blockedPolicy.Tier.Should().Be(WebTrustTier.None);
    }

    [Fact]
    public void ChangeTier_WhenTierIsUndefined_RejectsTheTransitionAndPreservesState()
    {
        // Arrange
        var policy = WebTrustPolicy.Create("honda.com", WebTrustTier.TierA, reason: "baseline");

        // Act
        var act = () => policy.ChangeTier((WebTrustTier)999, "invalid tier");

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("tier");
        policy.Tier.Should().Be(WebTrustTier.TierA);
        policy.Reason.Should().Be("baseline");
        policy.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public void MarkUpdated_WhenCalled_RecordsUpdateTimestamp()
    {
        // Arrange
        var policy = WebTrustPolicy.Create("honda.com", WebTrustTier.TierA);
        var beforeUpdate = DateTime.UtcNow;

        // Act
        policy.MarkUpdated();

        // Assert
        policy.UpdatedAt.Should().NotBeNull();
        policy.UpdatedAt!.Value.Should().BeOnOrAfter(beforeUpdate);
    }

    [Fact]
    public void Rehydrate_WhenPersistedStateViolatesInvariants_RejectsTheRow()
    {
        // Arrange
        var createdAt = DateTime.UtcNow;

        // Act
        var emptyId = () => WebTrustPolicy.Rehydrate(
            Guid.Empty, "honda.com", WebTrustTier.TierA, false, null, false, createdAt, null);
        var emptyPattern = () => WebTrustPolicy.Rehydrate(
            Guid.NewGuid(), "*. ", WebTrustTier.TierA, false, null, false, createdAt, null);
        var unknownTier = () => WebTrustPolicy.Rehydrate(
            Guid.NewGuid(), "honda.com", (WebTrustTier)999, false, null, false, createdAt, null);
        var untrustedAllowed = () => WebTrustPolicy.Rehydrate(
            Guid.NewGuid(), "honda.com", WebTrustTier.None, false, null, false, createdAt, null);

        // Assert
        emptyId.Should().Throw<ArgumentException>();
        emptyPattern.Should().Throw<ArgumentException>();
        unknownTier.Should().Throw<ArgumentOutOfRangeException>();
        untrustedAllowed.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Allow_WhenBlockedPolicyHasNoTier_RejectsInvalidAllowedState()
    {
        // Arrange
        var policy = WebTrustPolicy.Create("honda.com", WebTrustTier.None, isBlocked: true);

        // Act
        var act = () => policy.Allow();

        // Assert
        act.Should().Throw<InvalidOperationException>();
        policy.IsBlocked.Should().BeTrue();
    }
}
