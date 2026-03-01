namespace MotorcycleRAG.Application.Pipeline.Validators;

using MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Validates <see cref="IngestionJobStartRequest"/> before pipeline submission.
/// Returns a list of human-readable error strings (empty = valid).
/// </summary>
public sealed class IngestionJobValidator {
    private static readonly StringComparer OrdinalIgnoreCase = StringComparer.OrdinalIgnoreCase;

    private static readonly HashSet<string> AllowedDocumentTypes = new(OrdinalIgnoreCase)
    {
        "manual-pdf",
        "spec-dataset"
    };

    /// <summary>
    /// Validates the request and returns a list of validation errors.
    /// An empty list indicates the request is valid.
    /// </summary>
    public IReadOnlyList<string> Validate(IngestionJobStartRequest request) {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<string>();

        ValidateUploadId(request.UploadId, errors);
        ValidateDocumentType(request.DocumentType, errors);
        ValidateConfiguration(request, errors);

        return errors;
    }

    private static void ValidateUploadId(string uploadId, List<string> errors) {
        if (string.IsNullOrWhiteSpace(uploadId)) {
            errors.Add("UploadId is required.");
            return;
        }

        if (uploadId.Length > 500) {
            errors.Add("UploadId must not exceed 500 characters.");
        }

        if (uploadId.Contains('/') || uploadId.Contains('\\')) {
            errors.Add("UploadId must not contain path separators ('/' or '\\').");
        }
    }

    private static void ValidateDocumentType(string documentType, List<string> errors) {
        if (string.IsNullOrWhiteSpace(documentType)) {
            errors.Add("DocumentType is required.");
            return;
        }

        if (!AllowedDocumentTypes.Contains(documentType)) {
            errors.Add("DocumentType must be 'manual-pdf' or 'spec-dataset'.");
        }
    }

    private static void ValidateConfiguration(IngestionJobStartRequest request, List<string> errors) {
        if (OrdinalIgnoreCase.Equals(request.DocumentType, "manual-pdf") && request.Configuration is null) {
            errors.Add("Configuration is required when DocumentType is 'manual-pdf'.");
        }
    }
}
