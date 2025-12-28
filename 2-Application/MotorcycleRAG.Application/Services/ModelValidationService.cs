using System.ComponentModel.DataAnnotations;
using MotorcycleRAG.Contracts.Models;
using MotorcycleRAG.Domain.Models;

namespace MotorcycleRAG.Application.Services;

/// <summary>
/// Extension methods for string operations
/// </summary>
public static class StringExtensions
{
    /// <summary>
    /// Checks if a string contains any of the specified substrings
    /// </summary>
    public static bool ContainsAny(this string text, string[] substrings)
    {
        if (string.IsNullOrWhiteSpace(text) || substrings == null || substrings.Length == 0)
            return false;

        return substrings.Any(substring => text.Contains(substring, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Checks if a string starts with any of the specified prefixes
    /// </summary>
    public static bool StartsWithAny(this string text, string[] prefixes)
    {
        if (string.IsNullOrWhiteSpace(text) || prefixes == null || prefixes.Length == 0)
            return false;

        return prefixes.Any(prefix => text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Formats a string with invariant culture
    /// </summary>
    public static string FormatInvariant(this string format, params object[] args)
    {
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args);
    }
}

/// <summary>
/// Service for validating domain models
/// </summary>
public class ModelValidationService
{
    /// <summary>
    /// Validates a model and returns validation results
    /// </summary>
    /// <typeparam name="T">Type of model to validate</typeparam>
    /// <param name="model">Model instance to validate</param>
    /// <returns>Validation result with any errors</returns>
    public ValidationResult ValidateModel<T>(T model) where T : class
    {
        if (model == null)
        {
            return new ValidationResult
            {
                IsValid = false,
                Errors = new List<string> { "Model cannot be null" }
            };
        }

        var validationContext = new ValidationContext(model);
        var validationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        
        bool isValid = Validator.TryValidateObject(model, validationContext, validationResults, true);

        return new ValidationResult
        {
            IsValid = isValid,
            Errors = validationResults.Select(vr => vr.ErrorMessage ?? "Unknown validation error").ToList()
        };
    }

    /// <summary>
    /// Validates a MotorcycleSpecification with business rules
    /// </summary>
    /// <param name="specification">Motorcycle specification to validate</param>
    /// <returns>Validation result</returns>
    public ValidationResult ValidateMotorcycleSpecification(MotorcycleSpecification specification)
    {
        var result = ValidateModel(specification);
        
        if (!result.IsValid)
            return result;

        // Additional business rule validations
        var businessErrors = new List<string>();

        // Validate engine specifications if present
        if (specification.Engine != null)
        {
            var engineValidation = ValidateModel(specification.Engine);
            if (!engineValidation.IsValid)
            {
                businessErrors.AddRange(engineValidation.Errors.Select(e => $"Engine: {e}"));
            }

            // Business rule: Displacement should match horsepower range
            if (specification.Engine.DisplacementCC > 0 && specification.Engine.Horsepower > 0)
            {
                var expectedMinHp = specification.Engine.DisplacementCC / 20; // Rough estimate
                var expectedMaxHp = specification.Engine.DisplacementCC / 5;
                
                if (specification.Engine.Horsepower < expectedMinHp * 0.5 || 
                    specification.Engine.Horsepower > expectedMaxHp * 2)
                {
                    businessErrors.Add("Engine horsepower seems inconsistent with displacement");
                }
            }
        }

        // Validate performance metrics if present
        if (specification.Performance != null)
        {
            var performanceValidation = ValidateModel(specification.Performance);
            if (!performanceValidation.IsValid)
            {
                businessErrors.AddRange(performanceValidation.Errors.Select(e => $"Performance: {e}"));
            }
        }

        // Validate safety features if present
        if (specification.Safety != null)
        {
            var safetyValidation = ValidateModel(specification.Safety);
            if (!safetyValidation.IsValid)
            {
                businessErrors.AddRange(safetyValidation.Errors.Select(e => $"Safety: {e}"));
            }
        }

        // Validate pricing if present
        if (specification.Pricing != null)
        {
            var pricingValidation = ValidateModel(specification.Pricing);
            if (!pricingValidation.IsValid)
            {
                businessErrors.AddRange(pricingValidation.Errors.Select(e => $"Pricing: {e}"));
            }

            // Business rule: Price date should not be in the future
            if (specification.Pricing.PriceDate > DateTime.UtcNow.AddDays(1))
            {
                businessErrors.Add("Price date cannot be in the future");
            }
        }

        return new ValidationResult
        {
            IsValid = businessErrors.Count == 0,
            Errors = businessErrors
        };
    }

    /// <summary>
    /// Validates a MotorcycleDocument with content rules
    /// </summary>
    /// <param name="document">Document to validate</param>
    /// <returns>Validation result</returns>
    public ValidationResult ValidateMotorcycleDocument(MotorcycleDocument document)
    {
        var result = ValidateModel(document);
        
        if (!result.IsValid)
            return result;

        var businessErrors = new List<string>();

        // Validate content length (only if basic validation passed)
        if (result.IsValid && string.IsNullOrWhiteSpace(document.Content))
        {
            businessErrors.Add("Document content cannot be empty");
        }
        else if (document.Content.Length < 10)
        {
            businessErrors.Add("Document content is too short (minimum 10 characters)");
        }
        else if (document.Content.Length > 1000000) // 1MB limit
        {
            businessErrors.Add("Document content is too large (maximum 1MB)");
        }

        // Validate vector dimensions if present
        if (document.ContentVector != null)
        {
            if (document.ContentVector.Length == 0)
            {
                businessErrors.Add("Content vector cannot be empty if provided");
            }
            else if (document.ContentVector.Length != 3072) // text-embedding-3-large dimension
            {
                businessErrors.Add("Content vector must have 3072 dimensions for text-embedding-3-large model");
            }
        }

        // Validate metadata if present
        if (document.Metadata != null)
        {
            var metadataValidation = ValidateModel(document.Metadata);
            if (!metadataValidation.IsValid)
            {
                businessErrors.AddRange(metadataValidation.Errors.Select(e => $"Metadata: {e}"));
            }
        }

        return new ValidationResult
        {
            IsValid = businessErrors.Count == 0,
            Errors = businessErrors
        };
    }

    /// <summary>
    /// Validates a query request
    /// </summary>
    /// <param name="request">Query request to validate</param>
    /// <returns>Validation result</returns>
    public ValidationResult ValidateQueryRequest(MotorcycleQueryRequest request)
    {
        var result = ValidateModel(request);
        
        if (!result.IsValid)
            return result;

        var businessErrors = new List<string>();

        // Validate query content
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            businessErrors.Add("Query cannot be empty");
        }
        else if (request.Query.Length < 3)
        {
            businessErrors.Add("Query is too short (minimum 3 characters)");
        }

        // Validate preferences if present
        if (request.Preferences != null)
        {
            var preferencesValidation = ValidateModel(request.Preferences);
            if (!preferencesValidation.IsValid)
            {
                businessErrors.AddRange(preferencesValidation.Errors.Select(e => $"Preferences: {e}"));
            }

            // Business rules for preferences
            if (request.Preferences.MaxResults <= 0)
            {
                businessErrors.Add("MaxResults must be greater than 0");
            }
            else if (request.Preferences.MaxResults > 100)
            {
                businessErrors.Add("MaxResults cannot exceed 100");
            }

            if (request.Preferences.MinRelevanceScore < 0 || request.Preferences.MinRelevanceScore > 1)
            {
                businessErrors.Add("MinRelevanceScore must be between 0 and 1");
            }
        }

        return new ValidationResult
        {
            IsValid = businessErrors.Count == 0,
            Errors = businessErrors
        };
    }

    /// <summary>
    /// Validates citations and claim verification
    /// </summary>
    /// <param name="response">Query response to validate</param>
    /// <returns>Validation result</returns>
    public ValidationResult ValidateQueryResponseCitations(MotorcycleQueryResponse response)
    {
        var result = ValidateModel(response);
        
        if (!result.IsValid)
            return result;

        var businessErrors = new List<string>();

        // Validate query ID format
        if (string.IsNullOrWhiteSpace(response.QueryId) || response.QueryId.Length != 32 || response.QueryId.Contains("-"))
        {
            businessErrors.Add("QueryId must be a valid GUID without hyphens (N format)");
        }

        // Validate that response is not empty
        if (string.IsNullOrWhiteSpace(response.Response))
        {
            businessErrors.Add("Response cannot be empty");
        }

        // Validate metrics completeness
        if (response.Metrics == null)
        {
            businessErrors.Add("Metrics cannot be null");
        }
        else
        {
            // Check for reasonable metric values
            if (response.Metrics.TotalDuration < TimeSpan.Zero)
            {
                businessErrors.Add("TotalDuration cannot be negative");
            }

            if (response.Metrics.ResultsFound < 0)
            {
                businessErrors.Add("ResultsFound cannot be negative");
            }

            // Verify results count matches sources
            if (response.Sources != null && response.Metrics.ResultsFound != response.Sources.Length)
            {
                businessErrors.Add("$ResultsFound ({0}) does not match actual source count ({1})".FormatInvariant(
                    response.Metrics.ResultsFound, response.Sources.Length));
            }
        }

        // Validate citations for all sources
        if (response.Sources != null && response.Sources.Length > 0)
        {
            foreach (var source in response.Sources)
            {
                var sourceValidation = ValidateSearchResultCitation(source);
                if (!sourceValidation.IsValid)
                {
                    businessErrors.AddRange(sourceValidation.Errors.Select(e => $"Source validation: {e}"));
                }
            }
        }

        // Validate claim-citation consistency
        var citationValidation = ValidateClaimCitationConsistency(response);
        if (!citationValidation.IsValid)
        {
            businessErrors.AddRange(citationValidation.Errors);
        }

        return new ValidationResult
        {
            IsValid = businessErrors.Count == 0,
            Errors = businessErrors
        };
    }

    private ValidationResult ValidateSearchResultCitation(SearchResult result)
    {
        var errors = new List<string>();

        // Check if citation is required based on content
        if (ShouldHaveCitation(result))
        {
            if (result.Source.Citation == null)
            {
                errors.Add("$Result {0} is missing required citation".FormatInvariant(result.Id));
                return new ValidationResult { IsValid = false, Errors = errors };
            }

            // Validate citation completeness
            if (string.IsNullOrWhiteSpace(result.Source.Citation.SourceName))
            {
                errors.Add("$Result {0} citation is missing SourceName".FormatInvariant(result.Id));
            }

            if (result.Source.Citation.ConfidenceScore < 0 || result.Source.Citation.ConfidenceScore > 1)
            {
                errors.Add("$Result {0} citation has invalid ConfidenceScore".FormatInvariant(result.Id));
            }

            // Validate locator based on source type
            if (result.Source.Citation.Locator != null)
            {
                var locatorValidation = ValidateCitationLocator(result.Source.Citation);
                if (!locatorValidation.IsValid)
                {
                    errors.AddRange(locatorValidation.Errors.Select(e => $"Locator validation for {result.Id}: {e}"));
                }
            }
        }

        return new ValidationResult { IsValid = errors.Count == 0, Errors = errors };
    }

    private bool ShouldHaveCitation(SearchResult result)
    {
        // Results with high relevance scores or specific content types should have citations
        if (result.RelevanceScore >= 0.7f)
            return true;

        if (result.Content.ContainsAny(new[] { " is ", " has ", " are ", " was ", " were " }) &&
            (result.Content.Any(char.IsDigit) || result.Content.Contains("cc") || result.Content.Contains("hp")))
            return true;

        return false;
    }

    private ValidationResult ValidateCitationLocator(Citation citation)
    {
        var errors = new List<string>();

        switch (citation.SourceType)
        {
            case CitationSourceType.Dataset:
                if (citation.Locator is not DatasetCitationLocator datasetLocator)
                {
                    errors.Add("Dataset citation requires DatasetCitationLocator");
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(datasetLocator.DatasetName))
                        errors.Add("DatasetCitationLocator requires DatasetName");
                }
                break;

            case CitationSourceType.Website:
                if (citation.Locator is not WebsiteCitationLocator websiteLocator)
                {
                    errors.Add("Website citation requires WebsiteCitationLocator");
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(websiteLocator.Url))
                        errors.Add("WebsiteCitationLocator requires Url");
                }
                break;

            case CitationSourceType.ManualPdf:
                if (citation.Locator is not ManualPdfCitationLocator pdfLocator)
                {
                    errors.Add("PDF citation requires ManualPdfCitationLocator");
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(pdfLocator.DocumentId))
                        errors.Add("ManualPdfCitationLocator requires DocumentId");

                    if (pdfLocator.PageNumber < 1)
                        errors.Add("ManualPdfCitationLocator requires valid PageNumber");
                }
                break;

            // Other source types don't require specific locators
        }

        return new ValidationResult { IsValid = errors.Count == 0, Errors = errors };
    }

    private ValidationResult ValidateClaimCitationConsistency(MotorcycleQueryResponse response)
    {
        var errors = new List<string>();

        // This would be enhanced with actual claim extraction and verification
        // For now, we do basic checks

        // Check if response contains citation markers
        if (response.Response.Contains("[") && response.Response.Contains("]"))
        {
            // Extract citation markers like [1], [2-1], etc.
            var citationPattern = new System.Text.RegularExpressions.Regex(@"\[(\d+(?:-\d+)?)\]");
            var matches = citationPattern.Matches(response.Response);

            if (matches.Count > 0)
            {
                // Verify that cited sources exist
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    var citationRef = match.Groups[1].Value;
                    
                    // Simple validation - in production this would map to actual sources
                    if (!citationRef.Contains("-") && int.TryParse(citationRef, out var sourceIndex))
                    {
                        if (response.Sources == null || sourceIndex < 1 || sourceIndex > response.Sources.Length)
                        {
                            errors.Add("$Citation reference [{sourceIndex}] exceeds available sources count".FormatInvariant(sourceIndex));
                        }
                    }
                }
            }
        }

        return new ValidationResult { IsValid = errors.Count == 0, Errors = errors };
    }

    /// <summary>
    /// Validates that every factual claim has proper citation
    /// </summary>
    /// <param name="response">Query response to validate</param>
    /// <returns>Validation result</returns>
    public ValidationResult ValidateEveryClaimHasCitation(MotorcycleQueryResponse response)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(response.Response))
        {
            errors.Add("Cannot validate claims in empty response");
            return new ValidationResult { IsValid = false, Errors = errors };
        }

        // Extract potential factual claims from the response
        var potentialClaims = ExtractPotentialClaims(response.Response);

        if (potentialClaims.Length == 0)
        {
            return new ValidationResult { IsValid = true, Errors = errors };
        }

        // Check each claim for proper citation
        foreach (var claim in potentialClaims)
        {
            bool hasCitation = false;
            
            // Check if claim is followed by citation marker
            if (response.Response.Contains(claim))
            {
                var claimIndex = response.Response.IndexOf(claim, StringComparison.Ordinal);
                var remainingText = response.Response.Substring(claimIndex + claim.Length);
                
                // Look for citation markers within reasonable distance
                var nextSentenceEnd = remainingText.IndexOfAny(new[] { '.', '!', '?' });
                if (nextSentenceEnd > 0)
                {
                    var contextAfterClaim = remainingText.Substring(0, Math.Min(nextSentenceEnd + 1, 100));
                    hasCitation = contextAfterClaim.Contains("[") && contextAfterClaim.Contains("]");
                }
            }

            if (!hasCitation)
            {
                // Check if claim is qualified (contains words like "may", "might", "possibly", etc.)
                if (IsQualifiedClaim(claim))
                {
                    // Qualified claims don't require citations
                    continue;
                }

                // Check if this is a common knowledge statement
                if (IsCommonKnowledge(claim))
                {
                    continue;
                }

                errors.Add("$Factual claim requires citation or qualification: '{claim}'".FormatInvariant(claim));
            }
        }

        return new ValidationResult { IsValid = errors.Count == 0, Errors = errors };
    }

    private string[] ExtractPotentialClaims(string text)
    {
        // Split into sentences
        var sentences = text.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => s.Trim())
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .ToArray();

        // Filter for sentences that likely contain factual claims
        return sentences.Where(s =>
            // Contains factual indicators
            (s.ContainsAny(new[] { " is ", " has ", " are ", " was ", " were ", " produces ", " features ", " includes " }) ||
            // Contains specific measurements
            s.Any(char.IsDigit)) &&
            // Not a question or introductory phrase
            !s.StartsWithAny(new[] { "The ", "This ", "These ", "Those ", "A ", "An " }) &&
            // Not too short
            s.Length > 10
        ).ToArray();
    }

    private bool IsQualifiedClaim(string claim)
    {
        var qualifyingTerms = new[]
        {
            "may", "might", "could", "possibly", "potentially", "likely", "probably",
            "often", "sometimes", "typically", "generally", "usually", "can", "tend to"
        };

        return claim.ContainsAny(qualifyingTerms);
    }

    private bool IsCommonKnowledge(string claim)
    {
        // Simple common knowledge detection
        var commonKnowledgePatterns = new[]
        {
            "motorcycle", "vehicle", "engine", "wheels", "two-wheeled", "transportation",
            "ride", "driver", "passenger", "road", "street", "speed", "power"
        };

        // If claim only contains very basic terms, consider it common knowledge
        var words = claim.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                         .Select(w => w.ToLower().Trim('.', ',', ';', ':', '!', '?'))
                         .Where(w => w.Length > 2)
                         .ToArray();

        if (words.Length == 0) return false;

        // If most words are common knowledge terms
        var commonWordCount = words.Count(w => commonKnowledgePatterns.Contains(w));
        return commonWordCount >= words.Length * 0.7; // 70% common words
    }
}

/// <summary>
/// Result of model validation
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
}
