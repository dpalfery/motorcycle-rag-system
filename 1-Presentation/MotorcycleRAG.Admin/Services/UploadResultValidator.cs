using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Admin.Services;

/// <summary>
/// Validator for file upload API responses
/// Ensures all required fields are present and in valid format
/// </summary>
public static class UploadResultValidator
{
    /// <summary>
    /// Validates the response from a single file upload operation
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if response is null or has invalid data</exception>
    public static void ValidateFileUploadResult(FileUploadResult? result)
    {
        if (result == null)
            throw new InvalidOperationException("Upload response was null");

        if (string.IsNullOrWhiteSpace(result.FileId))
            throw new InvalidOperationException("Upload response missing FileId");

        if (!Guid.TryParse(result.FileId, out _))
            throw new InvalidOperationException($"Invalid FileId format: {result.FileId}");

        if (string.IsNullOrWhiteSpace(result.OriginalFileName))
            throw new InvalidOperationException("Upload response missing OriginalFileName");

        if (result.FileSize <= 0)
            throw new InvalidOperationException("Invalid file size");
    }

    /// <summary>
    /// Validates the response from a batch file upload operation
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if response is null or has invalid data</exception>
    public static void ValidateBatchFileUploadResult(BatchFileUploadResult? result)
    {
        if (result == null)
            throw new InvalidOperationException("Batch upload response was null");

        if (string.IsNullOrWhiteSpace(result.BatchId))
            throw new InvalidOperationException("Batch upload response missing BatchId");

        if (!Guid.TryParse(result.BatchId, out _))
            throw new InvalidOperationException($"Invalid BatchId format: {result.BatchId}");

        if (result.SuccessfulUploads < 0)
            throw new InvalidOperationException("Invalid successful uploads count");

        if (result.FailedUploads < 0)
            throw new InvalidOperationException("Invalid failed uploads count");

        if (result.TotalFiles < 0)
            throw new InvalidOperationException("Invalid total files count");

        if (result.TotalFiles != result.SuccessfulUploads + result.FailedUploads)
            throw new InvalidOperationException("Upload counts don't match total files");
    }
}
