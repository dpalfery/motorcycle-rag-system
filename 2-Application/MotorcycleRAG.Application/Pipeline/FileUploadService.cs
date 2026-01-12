using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace MotorcycleRAG.Application.Pipeline;

/// <summary>
/// Service for handling secure file uploads with validation
/// </summary>
public class FileUploadService : IFileUploadService {
    private readonly ILogger<FileUploadService> _logger;
    private readonly FileUploadConfiguration _config;
    private readonly ITelemetryService _telemetryService;

    public FileUploadService(
        IOptions<FileUploadConfiguration> config,
        ITelemetryService telemetryService,
        ILogger<FileUploadService> logger) {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(telemetryService);
        ArgumentNullException.ThrowIfNull(logger);
        _config = config.Value;
        _telemetryService = telemetryService;
        _logger = logger;
    }

    /// <summary>
    /// Sanitizes user-provided values for logging to prevent log injection attacks.
    /// Replaces newlines, carriage returns, and tabs with spaces.
    /// </summary>
    private static string SanitizeForLogging(string input) {
        if (string.IsNullOrEmpty(input)) {
            return input;
        }

        return input
            .Replace('\n', ' ')
            .Replace('\r', ' ')
            .Replace('\t', ' ');
    }
    public async Task<FileUploadResult> UploadFileAsync(Stream fileStream, FileMetadata metadata, FileUploadOptions options, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(fileStream);

        var result = new FileUploadResult {
            OriginalFileName = metadata.FileName,
            FileSize = metadata.ContentLength,
            ContentType = metadata.ContentType
        };

        try {
            _logger.LogInformation("Starting file upload for {FileName} ({Size} bytes)", SanitizeForLogging(metadata.FileName), metadata.ContentLength);

            // Validate the file
            result.ValidationResult = await ValidateFileAsync(fileStream, metadata, options);
            if (!result.ValidationResult.IsValid) {
                _logger.LogWarning("File validation failed for {FileName}: {Errors}",
                    SanitizeForLogging(metadata.FileName), string.Join(", ", result.ValidationResult.Errors));
                return result;
            }

            result.DetectedFileType = result.ValidationResult.DetectedFileType;

            // Generate unique filename if required, always sanitize to prevent path traversal
            var safeFileName = SanitizeFileName(metadata.FileName);
            result.StoredFileName = options.GenerateUniqueFileName
                ? GenerateUniqueFileName(safeFileName)
                : safeFileName;

            // Ensure upload directory exists
            var uploadPath = Path.Combine(_config.BaseUploadDirectory, options.UploadDirectory);
            if (!Directory.Exists(uploadPath)) {
                Directory.CreateDirectory(uploadPath);
            }

            // Save file to disk
            result.FilePath = Path.Combine(uploadPath, result.StoredFileName);

            using (var stream = new FileStream(result.FilePath, FileMode.Create)) {
                await fileStream.CopyToAsync(stream, cancellationToken);
            }

            // Add metadata
            result.Metadata["UploadedBy"] = "System"; // Could be extracted from user context
            result.Metadata["ContentType"] = metadata.ContentType;
            result.Metadata["OriginalSize"] = metadata.ContentLength;
            result.Metadata["ValidationResults"] = result.ValidationResult;

            _logger.LogInformation("File upload completed successfully: {StoredFileName}",
                SanitizeForLogging(result.StoredFileName));

            // Track telemetry
            _telemetryService.TrackEvent("FileUploaded", new Dictionary<string, string> {
                ["FileName"] = SanitizeForLogging(metadata.FileName),
                ["FileType"] = result.DetectedFileType.ToString(),
                ["FileSize"] = metadata.ContentLength.ToString()
            });

            return result;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Failed to upload file {FileName}", SanitizeForLogging(metadata.FileName));
            result.ValidationResult.AddError($"Upload failed: {ex.Message}");
            return result;
        }
    }
    public async Task<BatchFileUploadResult> UploadFilesAsync(IEnumerable<(Stream stream, FileMetadata metadata)> files, FileUploadOptions options, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(options);

        var result = new BatchFileUploadResult();
        var fileList = files.ToList();
        result.TotalFiles = fileList.Count;

        _logger.LogInformation("Starting batch file upload for {FileCount} files", result.TotalFiles);

        try {
            // Process files sequentially to avoid overwhelming the system
            foreach (var (stream, metadata) in fileList) {
                cancellationToken.ThrowIfCancellationRequested();

                var uploadResult = await UploadFileAsync(stream, metadata, options, cancellationToken);
                result.Results.Add(uploadResult);

                if (uploadResult.IsValid) {
                    result.SuccessfulUploads++;
                }
                else {
                    result.FailedUploads++;
                    foreach (var err in uploadResult.ValidationResult.Errors) {
                        result.Errors.Add(err);
                    }
                }
            }

            _logger.LogInformation("Batch file upload completed. Success: {Success}, Failed: {Failed}",
                result.SuccessfulUploads, result.FailedUploads);

            return result;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Batch file upload failed");
            result.Errors.Add($"Batch upload failed: {ex.Message}");
            return result;
        }
    }

    public async Task<FileValidationResult> ValidateFileAsync(Stream fileStream, FileMetadata metadata, FileUploadOptions options) {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(fileStream);

        var result = new FileValidationResult {
            ContentType = metadata.ContentType,
            FileSize = metadata.ContentLength
        };

        try {
            // Check file size
            if (metadata.ContentLength > options.MaxFileSizeBytes) {
                result.AddError($"File size ({metadata.ContentLength:N0} bytes) exceeds maximum allowed size ({options.MaxFileSizeBytes:N0} bytes)");
            }

            if (metadata.ContentLength == 0) {
                result.AddError("File is empty");
            }

            // Check file extension
            var extension = Path.GetExtension(metadata.FileName)?.ToUpperInvariant();
            if (string.IsNullOrEmpty(extension)) {
                result.AddError("File has no extension");
            }
            else if (!options.AllowedFileExtensions.Contains(extension)) {
                result.AddError($"File extension '{extension}' is not allowed. Allowed extensions: {string.Join(", ", options.AllowedFileExtensions)}");
            }

            // Check content type
            if (!options.AllowedContentTypes.Contains(metadata.ContentType)) {
                result.AddWarning($"Content type '{metadata.ContentType}' may not be supported");
            }

            // Detect file type
            result.DetectedFileType = DetectFileType(metadata.FileName, metadata.ContentType);

            // Validate file content if required
            if (options.ValidateFileContent && result.IsValid) {
                await ValidateFileContentAsync(fileStream, metadata, result);
            }

            return result;
        }
        catch (Exception ex) {
            result.AddError($"Validation failed: {ex.Message}");
            return result;
        }
    }

    public FileUploadConstraints GetUploadConstraints() {
        var constraints = new FileUploadConstraints {
            MaxFileSizeBytes = _config.MaxFileSizeBytes,
            MaxFileSizeDisplay = FormatFileSize(_config.MaxFileSizeBytes),
            MaxFilesPerBatch = _config.MaxFilesPerBatch
        };

        constraints.SupportedFileTypes.Add("CSV");
        constraints.SupportedFileTypes.Add("PDF");
        constraints.SupportedExtensions.Add(".csv");
        constraints.SupportedExtensions.Add(".pdf");
        constraints.FileTypeDescriptions["CSV"] = "Comma-separated values files containing motorcycle specifications";
        constraints.FileTypeDescriptions["PDF"] = "PDF documents containing motorcycle manuals and technical documentation";

        return constraints;
    }

    public async Task<bool> DeleteFileAsync(string fileId) {
        if (string.IsNullOrWhiteSpace(fileId)) return false;

        try {
            // In a real implementation, you would look up the file path by fileId
            // For now, assuming fileId is the file path
            if (File.Exists(fileId)) {
                File.Delete(fileId);
                _logger.LogInformation("File deleted: {FileName}", Path.GetFileName(fileId));
                return true;
            }

            _logger.LogWarning("File not found for deletion: {FileName}", Path.GetFileName(fileId));
            return false;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Failed to delete file: {FileName}", Path.GetFileName(fileId));
            return false;
        }
    }

    private static FileType DetectFileType(string fileName, string? contentType) {
        var extension = Path.GetExtension(fileName)?.ToUpperInvariant();

        return extension switch {
            ".CSV" when contentType?.Contains("csv", StringComparison.OrdinalIgnoreCase) == true || contentType?.Contains("text", StringComparison.OrdinalIgnoreCase) == true => FileType.CSV,
            ".PDF" when contentType?.Contains("pdf", StringComparison.OrdinalIgnoreCase) == true => FileType.PDF,
            _ => FileType.Unknown
        };
    }

    private async Task ValidateFileContentAsync(Stream fileStream, FileMetadata metadata, FileValidationResult result) {
        try {
            var buffer = new byte[1024];
            int bytesRead = await fileStream.ReadAsync(buffer.AsMemory(0, buffer.Length));

            if (bytesRead > 0) {
                var fileExtension = Path.GetExtension(metadata.FileName)?.ToUpperInvariant();

                if (fileExtension == ".PDF") {
                    ValidatePdfContent(buffer, bytesRead, result);
                }
                else if (fileExtension == ".CSV") {
                    ValidateCsvContent(buffer, bytesRead, result);
                }
            }
        }
        catch (Exception ex) {
            result.AddWarning($"Content validation failed: {ex.Message}");
        }
    }

    private static void ValidatePdfContent(byte[] buffer, int bytesRead, FileValidationResult result) {
        // PDF files should start with %PDF
        var pdfHeader = Encoding.ASCII.GetString(buffer, 0, Math.Min(4, bytesRead));
        if (!pdfHeader.StartsWith("%PDF", StringComparison.Ordinal)) {
            result.AddError("File does not appear to be a valid PDF (missing PDF header)");
        }
    }

    private static void ValidateCsvContent(byte[] buffer, int bytesRead, FileValidationResult result) {
        try {
            var content = Encoding.UTF8.GetString(buffer, 0, bytesRead);

            // Basic CSV validation - check for common delimiters
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 0) {
                var firstLine = lines[0];
                var commaCount = firstLine.Count(c => c == ',');
                var semicolonCount = firstLine.Count(c => c == ';');

                if (commaCount == 0 && semicolonCount == 0) {
                    result.AddWarning("File may not be a valid CSV (no common delimiters found in first line)");
                }

                result.Properties["EstimatedColumns"] = Math.Max(commaCount, semicolonCount) + 1;
                result.Properties["HasHeader"] = !char.IsDigit(firstLine.FirstOrDefault());
            }
        }
        catch (Exception ex) {
            result.AddWarning($"CSV validation failed: {ex.Message}");
        }
    }

    private static string GenerateUniqueFileName(string originalFileName) {
        var extension = Path.GetExtension(originalFileName);
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(originalFileName);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var uniqueId = Guid.NewGuid().ToString("N")[..8];

        return $"{nameWithoutExtension}_{timestamp}_{uniqueId}{extension}";
    }

    private static string SanitizeFileName(string fileName) {
        if (string.IsNullOrEmpty(fileName)) {
            return "unnamed_file";
        }

        // Strip path separators and drive letters
        var safeFileName = fileName.Replace(":", "_").Replace("/", "_").Replace("\\", "_");
        safeFileName = Path.GetFileName(safeFileName);

        // Remove invalid filename characters
        var invalidChars = Path.GetInvalidFileNameChars();
        safeFileName = new string(safeFileName
            .Where(c => !invalidChars.Contains(c))
            .ToArray());

        if (string.IsNullOrWhiteSpace(safeFileName)) {
            return "unnamed_file";
        }

        const int maxFileNameLength = 255;
        if (safeFileName.Length > maxFileNameLength) {
            var extension = Path.GetExtension(safeFileName);
            var nameWithoutExt = Path.GetFileNameWithoutExtension(safeFileName);
            var maxNameLength = maxFileNameLength - extension.Length;
            if (maxNameLength > 0) {
                safeFileName = nameWithoutExt[..maxNameLength] + extension;
            }
        }

        return safeFileName;
    }

    private static string FormatFileSize(long bytes) {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1) {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}

/// <summary>
/// Configuration for file upload service
/// </summary>
public class FileUploadConfiguration {
    public string BaseUploadDirectory { get; set; } = "uploads";
    public long MaxFileSizeBytes { get; set; } = 50 * 1024 * 1024; // 50MB
    public int MaxFilesPerBatch { get; set; } = 10;
    public bool EnableVirusScanning { get; set; }
    public IReadOnlyList<string> AllowedExtensions { get; init; } = new[] { ".CSV", ".PDF" };
    public IReadOnlyList<string> AllowedContentTypes { get; init; } = new[] { "text/csv", "application/csv", "application/pdf" };
}


