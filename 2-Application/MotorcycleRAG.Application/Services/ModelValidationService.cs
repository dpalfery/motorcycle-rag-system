using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Application.Services;

/// <summary>
/// Validates model outputs and ensures citation data integrity.
/// Provides best-effort validation for citation metadata without rejecting
/// responses when locator information is legitimately unavailable.
/// </summary>
public class ModelValidationService
{
    private readonly ILogger<ModelValidationService> _logger;

    public ModelValidationService(ILogger<ModelValidationService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Validates a motorcycle query response, including all citations.
    /// </summary>
    /// <param name="response">The response to validate</param>
    /// <returns>Validation result with any errors found</returns>
    public ValidationResult ValidateResponse(MotorcycleQueryResponse response)
    {
        if (response == null)
        {
            return ValidationResult.Failure("Response cannot be null");
        }

        var errors = new List<string>();

        // Validate each search result's citations
        if (response.Sources != null)
        {
            for (int i = 0; i < response.Sources.Length; i++)
            {
                var source = response.Sources[i];
                if (source?.Source?.Citation != null)
                {
                    var citationErrors = ValidateCitation(source.Source.Citation, i);
                    errors.AddRange(citationErrors);
                }
            }
        }

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }

    /// <summary>
    /// Validates a single citation object.
    /// </summary>
    /// <param name="citation">The citation to validate</param>
    /// <param name="sourceIndex">Index of the source for error reporting</param>
    /// <returns>List of validation errors (empty if valid)</returns>
    public List<string> ValidateCitation(Citation citation, int sourceIndex = -1)
    {
        var errors = new List<string>();

        if (citation == null)
        {
            return errors; // Null citations are acceptable (best-effort)
        }

        // Only validate manual/PDF citations with locator metadata
        if (citation.SourceType == CitationSourceType.ManualPdf && citation.Locator != null)
        {
            var locatorErrors = ValidateManualPdfLocator(citation.Locator, sourceIndex);
            errors.AddRange(locatorErrors);
        }

        return errors;
    }

    /// <summary>
    /// Validates a ManualPdfCitationLocator for required fields and well-formed data.
    /// This is a best-effort validation - does not reject when locator is unavailable.
    /// </summary>
    /// <param name="locator">The locator object (expected to be ManualPdfCitationLocator)</param>
    /// <param name="sourceIndex">Index of the source for error reporting</param>
    /// <returns>List of validation errors (empty if valid)</returns>
    private List<string> ValidateManualPdfLocator(object locator, int sourceIndex)
    {
        var errors = new List<string>();

        if (locator is not ManualPdfCitationLocator manualLocator)
        {
            // If locator is not the expected type, log but don't fail (best-effort)
            _logger.LogWarning(
                "Citation locator is not ManualPdfCitationLocator for source {SourceIndex}. Type: {LocatorType}",
                sourceIndex,
                locator?.GetType().Name ?? "null");
            return errors;
        }

        var sourcePrefix = sourceIndex >= 0 ? $"Source[{sourceIndex}]: " : string.Empty;

        // Rule 1: PageNumber OR PageRange must exist (at least one meaningful value)
        bool hasValidPageNumber = manualLocator.PageNumber > 0;
        bool hasValidPageRange = !string.IsNullOrWhiteSpace(manualLocator.PageRange);

        if (!hasValidPageNumber && !hasValidPageRange)
        {
            errors.Add($"{sourcePrefix}Manual citation must have either PageNumber (> 0) or PageRange (non-empty). DocumentId: {SanitizeLogValue(manualLocator.DocumentId)}");
        }

        // Rule 2: If SectionHeadings exists, it must be non-empty strings (trimmed)
        if (manualLocator.SectionHeadings != null && manualLocator.SectionHeadings.Length > 0)
        {
            var emptyHeadingIndices = new List<int>();
            for (int i = 0; i < manualLocator.SectionHeadings.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(manualLocator.SectionHeadings[i]))
                {
                    emptyHeadingIndices.Add(i);
                }
            }

            if (emptyHeadingIndices.Count > 0)
            {
                errors.Add($"{sourcePrefix}SectionHeadings contains empty or whitespace-only strings at indices: {string.Join(", ", emptyHeadingIndices)}. DocumentId: {SanitizeLogValue(manualLocator.DocumentId)}");
            }
        }

        // Rule 3: If SectionLevel exists, it must be within valid range (0-3)
        if (manualLocator.SectionLevel.HasValue)
        {
            const int minSectionLevel = 0;
            const int maxSectionLevel = 3;

            if (manualLocator.SectionLevel.Value < minSectionLevel || manualLocator.SectionLevel.Value > maxSectionLevel)
            {
                errors.Add($"{sourcePrefix}SectionLevel must be between {minSectionLevel} and {maxSectionLevel}. Actual: {manualLocator.SectionLevel.Value}. DocumentId: {SanitizeLogValue(manualLocator.DocumentId)}");
            }
        }

        return errors;
    }

    /// <summary>
    /// Sanitizes a value for logging to prevent leaking sensitive data.
    /// </summary>
    /// <param name="value">The value to sanitize</param>
    /// <returns>A sanitized version of the value (truncated if too long)</returns>
    private string SanitizeLogValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "[empty]";
        }

        // Truncate long values to prevent log bloat and potential data leakage
        const int maxLogLength = 48;
        if (value.Length > maxLogLength)
        {
            return value.Substring(0, maxLogLength) + "...";
        }

        return value;
    }
}

/// <summary>
/// Result of a validation operation.
/// </summary>
public class ValidationResult
{
    /// <summary>
    /// Whether validation passed.
    /// </summary>
    public bool IsValid { get; }

    /// <summary>
    /// List of validation error messages.
    /// </summary>
    public List<string> Errors { get; }

    private ValidationResult(bool isValid, List<string> errors)
    {
        IsValid = isValid;
        Errors = errors ?? new List<string>();
    }

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static ValidationResult Success()
    {
        return new ValidationResult(true, new List<string>());
    }

    /// <summary>
    /// Creates a failed validation result with errors.
    /// </summary>
    /// <param name="errors">List of error messages</param>
    public static ValidationResult Failure(List<string> errors)
    {
        return new ValidationResult(false, errors);
    }

    /// <summary>
    /// Creates a failed validation result with a single error.
    /// </summary>
    /// <param name="error">Error message</param>
    public static ValidationResult Failure(string error)
    {
        return new ValidationResult(false, new List<string> { error });
    }

    /// <summary>
    /// Gets a formatted error message string.
    /// </summary>
    public string GetErrorMessage()
    {
        return IsValid ? "Validation passed" : string.Join("; ", Errors);
    }
}
