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
public class ModelValidationService {
    private readonly ILogger<ModelValidationService> _logger;

    public ModelValidationService(ILogger<ModelValidationService> logger) {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>
    /// Validates a motorcycle query response, including all citations.
    /// </summary>
    /// <param name="response">The response to validate</param>
    /// <returns>Validation result with any errors found</returns>
    public ModelValidationResult ValidateResponse(MotorcycleQueryResponse response) {
        if (response == null) {
            return ModelValidationResult.Failure("Response cannot be null");
        }

        var errors = new List<string>();

        // Validate each search result's citations
        if (response.Sources != null) {
            for (int i = 0; i < response.Sources.Length; i++) {
                var source = response.Sources[i];
                if (source?.Source?.Citation != null) {
                    var citationErrors = ValidateCitation(source.Source.Citation, i);
                    errors.AddRange(citationErrors);
                }
            }
        }

        return errors.Count == 0
            ? ModelValidationResult.Success()
            : ModelValidationResult.Failure(errors);
    }

    public IReadOnlyList<string> ValidateCitation(Citation? citation) => ValidateCitation(citation, -1);

    /// <summary>
    /// Validates a single citation object with source index.
    /// </summary>
    /// <param name="citation">The citation to validate</param>
    /// <param name="sourceIndex">Index of the source for error reporting</param>
    /// <returns>List of validation errors (empty if valid)</returns>
    public IReadOnlyList<string> ValidateCitation(Citation? citation, int sourceIndex) {
        var errors = new List<string>();

        if (citation == null) {
            return errors; // Null citations are acceptable (best-effort)
        }

        // Only validate manual/PDF citations with locator metadata
        if (citation.SourceType == CitationSourceType.ManualPdf && citation.Locator != null) {
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
    private IReadOnlyList<string> ValidateManualPdfLocator(object locator, int sourceIndex) {
        if (locator is not ManualPdfCitationLocator manualLocator) {
            // If locator is not the expected type, log but don't fail (best-effort)
            _logger.LogWarning(
                "Citation locator is not ManualPdfCitationLocator for source {SourceIndex}. Type: {LocatorType}",
                sourceIndex,
                locator?.GetType().Name ?? "null");
            return Array.Empty<string>();
        }

        var errors = new List<string>();
        var sourcePrefix = sourceIndex >= 0 ? $"Source[{sourceIndex}]: " : string.Empty;

        ValidatePageInfo(manualLocator, sourcePrefix, errors);
        ValidateHeadings(manualLocator, sourcePrefix, errors);
        ValidateSectionLevel(manualLocator, sourcePrefix, errors);

        return errors;
    }

    private void ValidatePageInfo(ManualPdfCitationLocator locator, string prefix, List<string> errors) {
        bool hasValidPageNumber = locator.PageNumber > 0;
        bool hasValidPageRange = !string.IsNullOrWhiteSpace(locator.PageRange);

        if (!hasValidPageNumber && !hasValidPageRange) {
            var docId = FormatDocumentIdForError(locator.DocumentId);
            errors.Add($"{prefix}Manual citation must have either PageNumber (> 0) or PageRange (non-empty). DocumentId: {docId}");
        }
    }

    private static string FormatDocumentIdForError(string? documentId) {
        if (string.IsNullOrEmpty(documentId))
            return "[empty]";

        const int maxDocumentIdLength = 48;
        return documentId.Length > maxDocumentIdLength
            ? documentId[..maxDocumentIdLength] + "..."
            : documentId;
    }

    private void ValidateHeadings(ManualPdfCitationLocator locator, string prefix, List<string> errors) {
        if (locator.SectionHeadings != null && locator.SectionHeadings.Length > 0) {
            var emptyHeadingIndices = locator.SectionHeadings
                .Select((h, i) => string.IsNullOrWhiteSpace(h) ? i : -1)
                .Where(i => i != -1)
                .ToList();

            if (emptyHeadingIndices.Count > 0) {
                errors.Add($"{prefix}SectionHeadings contains empty or whitespace-only strings at indices: {string.Join(", ", emptyHeadingIndices)}. " +
                           $"DocumentId: {FormatDocumentIdForError(locator.DocumentId)}");
            }
        }
    }

    private void ValidateSectionLevel(ManualPdfCitationLocator locator, string prefix, List<string> errors) {
        if (locator.SectionLevel.HasValue) {
            const int minSectionLevel = 0;
            const int maxSectionLevel = 3;

            if (locator.SectionLevel.Value < minSectionLevel || locator.SectionLevel.Value > maxSectionLevel) {
                errors.Add($"{prefix}SectionLevel must be between {minSectionLevel} and {maxSectionLevel}. " +
                           $"Actual: {locator.SectionLevel.Value}. DocumentId: {FormatDocumentIdForError(locator.DocumentId)}");
            }
        }
    }

}
