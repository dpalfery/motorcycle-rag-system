using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Domain.Enums;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Application.Services.Web;

/// <summary>
/// Validates web sources using trust policies and credibility checks.
/// AI-based content quality validation removed; scoring is now handled by Foundry-hosted agents.
/// </summary>
public class WebSourceValidator
{
    private readonly IWebTrustPolicyStore? _trustPolicyStore;
    private readonly float _minCredibilityScore;
    private readonly ILogger<WebSourceValidator> _logger;

    public WebSourceValidator(
        IWebTrustPolicyStore? trustPolicyStore,
        IOptions<WebSearchOptions> options,
        ILogger<WebSourceValidator> logger)
        : this(
            trustPolicyStore,
            (options ?? throw new ArgumentNullException(nameof(options))).Value.MinCredibilityScore,
            logger)
    {
    }

    public WebSourceValidator(
        IWebTrustPolicyStore? trustPolicyStore,
        float minCredibilityScore,
        ILogger<WebSourceValidator> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        if (minCredibilityScore is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minCredibilityScore), minCredibilityScore, "minCredibilityScore must be between 0 and 1");
        }

        _trustPolicyStore = trustPolicyStore;
        _minCredibilityScore = minCredibilityScore;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<SearchResult>> ValidateResultsAsync(
        IReadOnlyCollection<SearchResult> results,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(results);

        var validatedResults = new List<SearchResult>();
        var seen = new HashSet<string>();

        foreach (var result in results)
        {
            var validation = await ValidateSingleResultAsync(result, cancellationToken);

            if (validation.IsValid)
            {
                EnhanceResultWithValidationMetadata(result, validation);

                var key = $"{result.Content.GetHashCode()}_{result.Source.SourceUrl?.GetHashCode()}";
                if (seen.Add(key))
                {
                    validatedResults.Add(result);
                }
            }
        }

        _logger.LogDebug("Validated {ValidCount}/{TotalCount} results", validatedResults.Count, results.Count);
        return validatedResults;
    }

    public async Task<bool> ValidateSourceAsync(string sourceName, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(content);

        try
        {
            var domain = string.Empty;
            if (Uri.TryCreate(sourceName, UriKind.Absolute, out var uri))
            {
                domain = ExtractDomain(uri);
            }
            
            var policyCheck = CheckTrustPolicy(domain);

            if (!policyCheck.IsAllowed)
            {
                return false;
            }

            var credibilityScore = GetCredibilityScore(new SearchResult { Content = content });
            if (credibilityScore < _minCredibilityScore)
            {
                return false;
            }

            var contentValidation = await ValidateContentQualityAsync(content, CancellationToken.None);
            return contentValidation.IsValid;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Source validation failed for {SourceName}", sourceName);
            return true; // Allow source if validation fails
        }
    }

    private async Task<ValidationResult> ValidateSingleResultAsync(
        SearchResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            // Check trust policy first
            var domain = string.Empty;
            if (Uri.TryCreate(result.Source.SourceUrl, UriKind.Absolute, out var uri))
            {
                domain = ExtractDomain(uri);
            }

            var policyCheck = CheckTrustPolicy(domain);

            if (!policyCheck.IsAllowed)
            {
                return ValidationResult.Rejected(policyCheck.Reason ?? "Policy check failed");
            }

            // Check credibility score
            var credibilityScore = GetCredibilityScore(result);
            if (credibilityScore < _minCredibilityScore)
            {
                return ValidationResult.Rejected($"Credibility score {credibilityScore} below threshold {_minCredibilityScore}");
            }

            // AI-based content quality validation
            var contentValidation = await ValidateContentQualityAsync(result.Content, cancellationToken);

            return ValidationResult.Approved(contentValidation, policyCheck.Tier);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Validation failed for result, including anyway");
            return ValidationResult.ApprovedWithError(ex.Message);
        }
    }

    private (bool IsAllowed, WebTrustTier Tier, string? Reason) CheckTrustPolicy(string domain)
    {
        if (_trustPolicyStore == null)
        {
            return (true, WebTrustTier.None, null);
        }

        if (string.IsNullOrWhiteSpace(domain))
        {
            return (false, WebTrustTier.None, "Domain is empty");
        }

        var policy = _trustPolicyStore.GetPolicyForDomain(domain);

        if (policy == null)
        {
            return (false, WebTrustTier.None, $"Domain {domain} not on allowlist");
        }

        if (policy.IsBlocked)
        {
            return (false, policy.Tier, $"Domain blocked: {policy.Reason}");
        }

        return (true, policy.Tier, null);
    }

    private Task<ContentValidation> ValidateContentQualityAsync(string content, CancellationToken cancellationToken)
    {
        // Content quality scoring is now handled by Foundry-hosted agents (score_content tool).
        return Task.FromResult(new ContentValidation { IsValid = true, QualityScore = 0.7f });
    }

#pragma warning disable S4040 // Use ToLowerInvariant for culture-invariant host normalization
    private string ExtractDomain(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri.Host.ToLowerInvariant();
    }
#pragma warning restore S4040

    private float GetCredibilityScore(SearchResult result)
    {
        return result.Metadata.TryGetValue("credibilityScore", out var score)
            ? Convert.ToSingle(score)
            : 0.5f;
    }

    private void EnhanceResultWithValidationMetadata(SearchResult result, ValidationResult validation)
    {
        result.Metadata["contentQuality"] = validation.QualityScore;
        result.Metadata["validationPassed"] = validation.IsValid;
        result.Metadata["trustTierMultiplier"] = validation.TierMultiplier;
        result.Metadata["domainTrustTier"] = validation.Tier.ToString();

        result.RelevanceScore *= validation.TierMultiplier;
        result.RelevanceScore *= Math.Max(validation.QualityMultiplier, 0.7f);
    }
}

public class ValidationResult
{
    public bool IsValid { get; init; }
    public string? RejectionReason { get; init; }
    public float QualityScore { get; init; }
    public float QualityMultiplier => Math.Max(0.5f, QualityScore);
    public WebTrustTier Tier { get; init; }
    public float TierMultiplier { get; init; }

    public static ValidationResult Approved(ContentValidation contentValidation, WebTrustTier tier)
    {
        ArgumentNullException.ThrowIfNull(contentValidation);

        return new ValidationResult
        {
            IsValid = true,
            QualityScore = contentValidation.QualityScore,
            Tier = tier,
            TierMultiplier = GetTierMultiplier(tier)
        };
    }

    public static ValidationResult Rejected(string reason)
    {
        return new ValidationResult { IsValid = false, RejectionReason = reason };
    }

    public static ValidationResult ApprovedWithError(string error)
    {
        return new ValidationResult
        {
            IsValid = true,
            QualityScore = 0.7f,
            Tier = WebTrustTier.None,
            TierMultiplier = 1.0f
        };
    }

    private static float GetTierMultiplier(WebTrustTier tier)
    {
        return tier switch
        {
            WebTrustTier.TierA => 1.5f,
            WebTrustTier.TierB => 1.1f,
            WebTrustTier.TierC => 0.9f,
            _ => 1.0f
        };
    }
}

public class ContentValidation
{
    public bool IsValid { get; set; }
    public float QualityScore { get; set; }
    public string Reasoning { get; set; } = string.Empty;
}
