using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Application.Services.Web;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.UnitTests.Services.Web;

public sealed class WebSourceValidatorTests
{
    [Fact]
    public async Task ValidateResultsAsync_WhenResultsAreAllowed_EnhancesAndDeDuplicatesResults()
    {
        var validator = CreateValidator(trustPolicyStore: null, minimumScore: 0.5f);
        var first = CreateResult("same content", "https://example.test/a", relevance: 1f);
        var duplicate = CreateResult("same content", "https://example.test/a", relevance: 0.8f);
        var second = CreateResult("different content", "https://example.test/b", relevance: 0.6f);

        var results = await validator.ValidateResultsAsync([first, duplicate, second], CancellationToken.None);

        results.Should().BeEquivalentTo([first, second]);
        first.Metadata["contentQuality"].Should().Be(0.7f);
        first.Metadata["validationPassed"].Should().Be(true);
        first.Metadata["domainTrustTier"].Should().Be(WebTrustTier.None.ToString());
        first.RelevanceScore.Should().BeApproximately(0.7f, 0.0001f);
        second.RelevanceScore.Should().BeApproximately(0.42f, 0.0001f);
    }

    [Fact]
    public async Task ValidateResultsAsync_WhenPolicyAllowsTierAAndCredibilityIsSufficient_AppliesTrustMultiplier()
    {
        var policies = CreatePolicyStore();
        policies.Setup(store => store.GetPolicyForDomain("official.example"))
            .Returns(new WebTrustPolicy { Tier = WebTrustTier.TierA });
        var validator = CreateValidator(policies.Object, minimumScore: 0.8f);
        var result = CreateResult("content", "https://official.example/article", relevance: 1f, credibilityScore: 0.8f);

        var results = await validator.ValidateResultsAsync([result], CancellationToken.None);

        results.Should().ContainSingle().Which.Should().BeSameAs(result);
        result.Metadata["domainTrustTier"].Should().Be(WebTrustTier.TierA.ToString());
        result.Metadata["trustTierMultiplier"].Should().Be(1.5f);
        result.RelevanceScore.Should().BeApproximately(1.05f, 0.0001f);
    }

    [Fact]
    public async Task ValidateResultsAsync_WhenPolicyRejectsOrCredibilityIsLow_ExcludesResults()
    {
        var policies = CreatePolicyStore();
        policies.Setup(store => store.GetPolicyForDomain("missing.example")).Returns((WebTrustPolicy?)null);
        policies.Setup(store => store.GetPolicyForDomain("blocked.example"))
            .Returns(new WebTrustPolicy { IsBlocked = true, Tier = WebTrustTier.TierB, Reason = "blocked" });
        policies.Setup(store => store.GetPolicyForDomain("low.example"))
            .Returns(new WebTrustPolicy { Tier = WebTrustTier.TierC });
        var validator = CreateValidator(policies.Object, minimumScore: 0.8f);
        var missing = CreateResult("missing", "https://missing.example", credibilityScore: 1f);
        var blocked = CreateResult("blocked", "https://blocked.example", credibilityScore: 1f);
        var low = CreateResult("low", "https://low.example", credibilityScore: 0.7f);

        var results = await validator.ValidateResultsAsync([missing, blocked, low], CancellationToken.None);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateResultsAsync_WhenResultValidationThrows_IncludesResultWithFallbackMetadata()
    {
        var validator = CreateValidator(trustPolicyStore: null, minimumScore: 0f);
        var result = CreateResult("content", "https://example.test", credibilityScore: "not-a-number");

        var results = await validator.ValidateResultsAsync([result], CancellationToken.None);

        results.Should().ContainSingle().Which.Should().BeSameAs(result);
        result.Metadata["validationPassed"].Should().Be(true);
        result.RelevanceScore.Should().BeApproximately(0.7f, 0.0001f);
    }

    [Fact]
    public async Task ValidateResultsAsync_WhenInputIsNull_ThrowsArgumentNullException()
    {
        var validator = CreateValidator(trustPolicyStore: null, minimumScore: 0f);

        var act = () => validator.ValidateResultsAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ValidateSourceAsync_WhenPolicyAndCredibilityAllowSource_ReturnsTrue()
    {
        var policies = CreatePolicyStore();
        policies.Setup(store => store.GetPolicyForDomain("allowed.example"))
            .Returns(new WebTrustPolicy { Tier = WebTrustTier.TierB });
        var validator = CreateValidator(policies.Object, minimumScore: 0.5f);

        var valid = await validator.ValidateSourceAsync("https://allowed.example/path", "content");

        valid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateSourceAsync_WhenPolicyOrCredibilityRejectsSource_ReturnsFalse()
    {
        var policies = CreatePolicyStore();
        policies.Setup(store => store.GetPolicyForDomain("blocked.example"))
            .Returns(new WebTrustPolicy { IsBlocked = true, Reason = "blocked" });
        var policyValidator = CreateValidator(policies.Object, minimumScore: 0.5f);
        var credibilityValidator = CreateValidator(trustPolicyStore: null, minimumScore: 0.6f);

        var blocked = await policyValidator.ValidateSourceAsync("https://blocked.example", "content");
        var lowCredibility = await credibilityValidator.ValidateSourceAsync("not-a-url", "content");

        blocked.Should().BeFalse();
        lowCredibility.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateSourceAsync_WhenDomainIsMissingOrPolicyFailsOpen_ReturnsExpectedSafetyResult()
    {
        var missingDomainPolicies = CreatePolicyStore();
        var missingDomainValidator = CreateValidator(missingDomainPolicies.Object, minimumScore: 0f);
        var failingPolicies = CreatePolicyStore();
        failingPolicies.Setup(store => store.GetPolicyForDomain("failure.example"))
            .Throws(new InvalidOperationException("policy store unavailable"));
        var failingValidator = CreateValidator(failingPolicies.Object, minimumScore: 0f);

        var missingDomain = await missingDomainValidator.ValidateSourceAsync("not-a-url", "content");
        var failure = await failingValidator.ValidateSourceAsync("https://failure.example", "content");

        missingDomain.Should().BeFalse();
        failure.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateSourceAsync_WhenArgumentsAreInvalid_Throws()
    {
        var validator = CreateValidator(trustPolicyStore: null, minimumScore: 0f);

        var missingName = () => validator.ValidateSourceAsync(" ", "content");
        var missingContent = () => validator.ValidateSourceAsync("source", null!);

        await missingName.Should().ThrowAsync<ArgumentException>();
        await missingContent.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WhenOptionsOrLoggerAreInvalid_Throws()
    {
        var policies = CreatePolicyStore().Object;

        ((Action)(() => new WebSourceValidator(policies, null!, NullLogger<WebSourceValidator>.Instance)))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => new WebSourceValidator(policies, -0.1f, NullLogger<WebSourceValidator>.Instance)))
            .Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => new WebSourceValidator(policies, 1.1f, NullLogger<WebSourceValidator>.Instance)))
            .Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => new WebSourceValidator(policies, 0.5f, null!)))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WhenOptionsAreValid_UsesConfiguredThreshold()
    {
        var validator = new WebSourceValidator(
            trustPolicyStore: null,
            Options.Create(new WebSearchOptions { MinCredibilityScore = 0.5f }),
            NullLogger<WebSourceValidator>.Instance);

        validator.Should().NotBeNull();
    }

    private static WebSourceValidator CreateValidator(
        IWebTrustPolicyStore? trustPolicyStore,
        float minimumScore) => new(
        trustPolicyStore,
        minimumScore,
        NullLogger<WebSourceValidator>.Instance);

    private static Mock<IWebTrustPolicyStore> CreatePolicyStore() => new(MockBehavior.Strict);

    private static SearchResult CreateResult(
        string content,
        string sourceUrl,
        float relevance = 1f,
        object? credibilityScore = null)
    {
        var result = new SearchResult
        {
            Id = Guid.NewGuid().ToString("N"),
            Content = content,
            RelevanceScore = relevance,
            Source = new SearchSource { SourceName = "unit", SourceUrl = sourceUrl },
        };

        if (credibilityScore is not null)
        {
            result.Metadata["credibilityScore"] = credibilityScore;
        }

        return result;
    }
}
