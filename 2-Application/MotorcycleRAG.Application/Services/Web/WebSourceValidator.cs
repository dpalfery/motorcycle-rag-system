using System.Text.Json;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Enums;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Application.Services.Web;

/// <summary>
/// Validates web sources using trust policies and AI-based credibility checks
/// </summary>
public class WebSourceValidator
{
    private readonly IAzureOpenAIClient _openAIClient;
    private readonly IWebTrustPolicyStore? _trustPolicyStore;
    private readonly float _minCredibilityScore;
    private readonly string _validationModel;
    private readonly ILogger<WebSourceValidator> _logger;

    public WebSourceValidator(
        IAzureOpenAIClient openAIClient,
        IWebTrustPolicyStore? trustPolicyStore,
        float minCredibilityScore,
        string validationModel,
        ILogger<WebSourceValidator> logger)
    {
        _openAIClient = openAIClient;
        _trustPolicyStore = trustPolicyStore;
        _minCredibilityScore = minCredibilityScore;
        _validationModel = validationModel;
        _logger = logger;
    }

    public async Task<List<SearchResult>> ValidateResultsAsync(
        List<SearchResult> results,
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
        ArgumentNullException.ThrowIfNull(content);

        try
        {
            var domain = ExtractDomain(sourceName);
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
            var domain = ExtractDomain(result.Source.SourceUrl);
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

    private async Task<ContentValidation> ValidateContentQualityAsync(string content, CancellationToken cancellationToken)
    {
        try
        {
            var prompt = $@"
Analyze this motorcycle-related content for quality and accuracy:

Content: {content.AsSpan(0, Math.Min(content.Length, 300)).ToString()}

Rate content on a scale of 0.0 to 1.0. Respond with only JSON:
{{
  ""qualityScore"": 0.0-1.0,
  ""isValid"": true/false,
  ""reasoning"": ""brief explanation""
}}
";

            var response = await _openAIClient.GetChatCompletionAsync(_validationModel, prompt, cancellationToken);
            var validation = JsonSerializer.Deserialize<ContentValidation>(response);
            return validation ?? new ContentValidation { IsValid = true, QualityScore = 0.7f };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI validation failed, assuming valid");
            return new ContentValidation { IsValid = true, QualityScore = 0.7f };
        }
    }

    private string ExtractDomain(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }

        return uri.Host.ToUpperInvariant();
    }

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