using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;
using MotorcycleRAG.Persistence.Configuration;

namespace MotorcycleRAG.UnitTests.Persistence.Configuration;

public sealed class WebTrustPolicyStoreTests
{
    private static WebTrustPolicyStore CreateSut() =>
        new(NullLogger<WebTrustPolicyStore>.Instance);

    [Fact]
    public void Constructor_WithDefaults_ExposesTrustedOemAndMediaPolicies()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var hondaPolicy = sut.GetPolicyForDomain("powersports.honda.com");
        var tierAPolicies = sut.GetPoliciesByTier(WebTrustTier.TierA);
        var tierBPolicies = sut.GetPoliciesByTier(WebTrustTier.TierB);

        // Assert
        hondaPolicy.Should().NotBeNull();
        hondaPolicy!.Tier.Should().Be(WebTrustTier.TierA);
        sut.IsDomainAllowed("powersports.honda.com", WebTrustTier.TierA).Should().BeTrue();
        tierAPolicies.Should().HaveCount(6);
        tierBPolicies.Should().HaveCount(3);
        sut.GetAllPolicies().Should().HaveCount(9);
    }

    [Fact]
    public void GetPolicyForDomain_WithCustomExactAndWildcardPolicies_RespectsSubdomainRules()
    {
        // Arrange
        var sut = CreateSut();
        var exactPolicy = CreatePolicy("www.example.com", WebTrustTier.TierB, allowSubdomains: false);
        var wildcardPolicy = CreatePolicy("*.docs.example", WebTrustTier.TierC, allowSubdomains: false);
        sut.AddOrUpdatePolicy(exactPolicy);
        sut.AddOrUpdatePolicy(wildcardPolicy);

        // Act
        var exact = sut.GetPolicyForDomain("www.example.com");
        var wildcardRoot = sut.GetPolicyForDomain("docs.example");
        var wildcardSubdomain = sut.GetPolicyForDomain("api.docs.example");

        // Assert
        exact.Should().BeSameAs(exactPolicy);
        wildcardRoot.Should().BeSameAs(wildcardPolicy);
        wildcardSubdomain.Should().BeNull();
        sut.GetPolicyForDomain("").Should().BeNull();
        sut.GetPolicyForDomain("unknown.example").Should().BeNull();
    }

    [Fact]
    public void IsDomainAllowed_WithBlockedAndLowTierPolicies_RejectsThemAndMaintainsAllowlists()
    {
        // Arrange
        var sut = CreateSut();
        var allowed = CreatePolicy("allowed.example", WebTrustTier.TierB, allowSubdomains: true);
        var blocked = CreatePolicy("blocked.example", WebTrustTier.TierA, isBlocked: true);
        var untrusted = CreatePolicy("untrusted.example", WebTrustTier.None);
        sut.AddOrUpdatePolicy(allowed);
        sut.AddOrUpdatePolicy(blocked);
        sut.AddOrUpdatePolicy(untrusted);

        // Act
        var tierBAllowlist = sut.GetAllowlistForTier(WebTrustTier.TierB);

        // Assert
        sut.IsDomainAllowed("allowed.example", WebTrustTier.TierB).Should().BeTrue();
        sut.IsDomainAllowed("allowed.example", WebTrustTier.TierA).Should().BeFalse();
        sut.IsDomainAllowed("blocked.example", WebTrustTier.TierC).Should().BeFalse();
        sut.IsDomainAllowed("untrusted.example", WebTrustTier.None).Should().BeTrue();
        tierBAllowlist.Should().Contain("allowed.example");
        sut.GetAllowlistForTier(WebTrustTier.TierC).Should().BeEmpty();
    }

    [Fact]
    public void RemovePolicy_WithExistingAndMissingPolicies_UpdatesTheStoreAndAllowlist()
    {
        // Arrange
        var sut = CreateSut();
        var policy = CreatePolicy("remove.example", WebTrustTier.TierC);
        sut.AddOrUpdatePolicy(policy);

        // Act
        var removed = sut.RemovePolicy(policy.DomainPattern);
        var missing = sut.RemovePolicy(policy.DomainPattern);

        // Assert
        removed.Should().BeTrue();
        missing.Should().BeFalse();
        sut.GetPolicyForDomain(policy.DomainPattern).Should().BeNull();
        sut.GetAllowlistForTier(WebTrustTier.TierC).Should().NotContain(policy.DomainPattern);
    }

    [Fact]
    public void AddOrUpdatePolicy_WithNullPolicy_ThrowsArgumentNullException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.AddOrUpdatePolicy(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    private static WebTrustPolicy CreatePolicy(
        string domainPattern,
        WebTrustTier tier,
        bool isBlocked = false,
        bool allowSubdomains = false) =>
        new()
        {
            Id = Guid.NewGuid(),
            DomainPattern = domainPattern,
            Tier = tier,
            IsBlocked = isBlocked,
            AllowSubdomains = allowSubdomains,
            Reason = "Unit test policy",
        };
}
