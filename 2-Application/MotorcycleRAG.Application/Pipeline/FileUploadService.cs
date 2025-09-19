using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Models;
using System.Text;

namespace MotorcycleRAG.Application.Pipeline;

/// <summary>
/// Service for handling secure file uploads with validation
/// </summary>
public class FileUploadService : IFileUploadService
{
    private readonly ILogger<FileUploadService> _logger;
    private readonly FileUploadConfiguration _config;
    private readonly ITelemetryService _telemetryService;

    public FileUploadService(
        IOptions<FileUploadConfiguration> config,
        ITelemetryService telemetryService,
        ILogger<FileUploadService> logger)
    {
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _telemetryService = telemetryService ?? throw new ArgumentNullException(nameof(telemetryService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<FileUploadResult> UploadFileAsync(IFormFile file, FileUploadOptions options, CancellationToken cancellationToken = default)
    {
        var result = new FileUploadResult
        {
            OriginalFileName = file.FileName,
            FileSize = file.Length,
            ContentType = file.ContentType
        };

        try
        {
            _logger.LogInformation("Starting file upload for {FileName} ({Size} bytes)", file.FileName, file.Length);

            // Validate the file
            result.ValidationResult = await ValidateFileAsync(file, options);
            if (!result.ValidationResult.IsValid)
            {
                _logger.LogWarning("File validation failed for {FileName}: {Errors}", 
                    file.FileName, string.Join(", ", result.ValidationResult.Errors));
                return result;
            }

            result.DetectedFileType = result.ValidationResult.DetectedFileType;

            // Generate unique filename if required
            result.StoredFileName = options.GenerateUniqueFileName 
                ? GenerateUniqueFileName(file.FileName)
                : file.FileName;

            // Ensure upload directory exists
            var uploadPath = Path.Combine(_config.BaseUploadDirectory, options.UploadDirectory);
            Directory.CreateDirectory(uploadPath);

            // Save file to disk
            result.FilePath = Path.Combine(uploadPath, result.StoredFileName);
            
            using (var stream = new FileStream(result.FilePath, FileMode.Create))
            {
                await file.CopyToAsync(stream, cancellationToken);
            }

            // Add metadata
            result.Metadata["UploadedBy"] = "System"; // Could be extracted from user context
            result.Metadata["ContentType"] = file.ContentType;
            result.Metadata["OriginalSize"] = file.Length;
            result.Metadata["ValidationResults"] = result.ValidationResult;

            _logger.LogInformation("File upload completed successfully: {StoredFileName} at {FilePath}", 
                result.StoredFileName, result.FilePath);

            // Track telemetry
            _telemetryService.TrackEvent("FileUploaded", new Dictionary<string, string>
            {
                ["FileName"] = file.FileName,
                ["FileType"] = result.DetectedFileType.ToString(),
                ["FileSize"] = file.Length.ToString()
            });

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload file {FileName}", file.FileName);
            result.ValidationResult.AddError($"Upload failed: {ex.Message}");
            return result;
        }
    }

    public async Task<BatchFileUploadResult> UploadFilesAsync(IEnumerable<IFormFile> files, FileUploadOptions options, CancellationToken cancellationToken = default)
    {
        var result = new BatchFileUploadResult();
        var fileList = files.ToList();
        result.TotalFiles = fileList.Count;

        _logger.LogInformation("Starting batch file upload for {FileCount} files", result.TotalFiles);

        try
        {
            // Process files sequentially to avoid overwhelming the system
            foreach (var file in fileList)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var uploadResult = await UploadFileAsync(file, options, cancellationToken);
                result.Results.Add(uploadResult);

                if (uploadResult.IsValid)
                {
                    result.SuccessfulUploads++;
                }
                else
                {
                    result.FailedUploads++;
                    result.Errors.AddRange(uploadResult.ValidationResult.Errors);
                }
            }

            _logger.LogInformation("Batch file upload completed. Success: {Success}, Failed: {Failed}", 
                result.SuccessfulUploads, result.FailedUploads);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch file upload failed");
            result.Errors.Add($"Batch upload failed: {ex.Message}");
            return result;
        }
    }

    public async Task<FileValidationResult> ValidateFileAsync(IFormFile file, FileUploadOptions options)
    {
        var result = new FileValidationResult
        {
            ContentType = file.ContentType,
            FileSize = file.Length
        };

        try
        {
            // Check file size
            if (file.Length > options.MaxFileSizeBytes)
            {
                result.AddError($"File size ({file.Length:N0} bytes) exceeds maximum allowed size ({options.MaxFileSizeBytes:N0} bytes)");
            }

            if (file.Length == 0)
            {
                result.AddError("File is empty");
            }

            // Check file extension
            var extension = Path.GetExtension(file.FileName)?.ToLowerInvariant();
            if (string.IsNullOrEmpty(extension))
            {
                result.AddError("File has no extension");
            }
            else if (!options.AllowedFileExtensions.Contains(extension))
            {
                result.AddError($"File extension '{extension}' is not allowed. Allowed extensions: {string.Join(", ", options.AllowedFileExtensions)}");
            }

            // Check content type
            if (!options.AllowedContentTypes.Contains(file.ContentType))
            {
                result.AddWarning($"Content type '{file.ContentType}' may not be supported");
            }

            // Detect file type
            result.DetectedFileType = DetectFileType(file.FileName, file.ContentType);

            // Validate file content if required
            if (options.ValidateFileContent && result.IsValid)
            {
                await ValidateFileContentAsync(file, result);
            }

            return result;
        }
        catch (Exception ex)
        {
            result.AddError($"Validation failed: {ex.Message}");
            return result;
        }
    }

    public FileUploadConstraints GetUploadConstraints()
    {
        return new FileUploadConstraints
        {
            MaxFileSizeBytes = _config.MaxFileSizeBytes,
            MaxFileSizeDisplay = FormatFileSize(_config.MaxFileSizeBytes),
            SupportedFileTypes = new List<string> { "CSV", "PDF" },
            SupportedExtensions = new List<string> { ".csv", ".pdf" },
            MaxFilesPerBatch = _config.MaxFilesPerBatch,
            FileTypeDescriptions = new Dictionary<string, string>
            {
                ["CSV"] = "Comma-separated values files containing motorcycle specifications",
                ["PDF"] = "PDF documents containing motorcycle manuals and technical documentation"
            }
        };
    }

    public async Task<bool> DeleteFileAsync(string fileId)
    {
        try
        {
            // In a real implementation, you would look up the file path by fileId
            // For now, assuming fileId is the file path
            if (File.Exists(fileId))
            {
                File.Delete(fileId);
                _logger.LogInformation("File deleted: {FileId}", fileId);
                return true;
            }

            _logger.LogWarning("File not found for deletion: {FileId}", fileId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete file: {FileId}", fileId);
            return false;
        }
    }

    private FileType DetectFileType(string fileName, string contentType)
    {
        var extension = Path.GetExtension(fileName)?.ToLowerInvariant();
        
        return extension switch
        {
            ".csv" when contentType?.Contains("csv") == true || contentType?.Contains("text") == true => FileType.CSV,
            ".pdf" when contentType?.Contains("pdf") == true => FileType.PDF,
            _ => FileType.Unknown
        };
    }

    private async Task ValidateFileContentAsync(IFormFile file, FileValidationResult result)
    {
        try
        {
            using var stream = file.OpenReadStream();
            var buffer = new byte[1024];
            var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);

            if (bytesRead > 0)
            {
                var fileExtension = Path.GetExtension(file.FileName)?.ToLowerInvariant();

                switch (fileExtension)
                {
                    case ".pdf":
                        ValidatePdfContent(buffer, bytesRead, result);
                        break;
                    case ".csv":
                        ValidateCsvContent(buffer, bytesRead, result);
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            result.AddWarning($"Content validation failed: {ex.Message}");
        }
    }

    private void ValidatePdfContent(byte[] buffer, int bytesRead, FileValidationResult result)
    {
        // PDF files should start with %PDF
        var pdfHeader = Encoding.ASCII.GetString(buffer, 0, Math.Min(4, bytesRead));
        if (!pdfHeader.StartsWith("%PDF"))
        {
            result.AddError("File does not appear to be a valid PDF (missing PDF header)");
        }
    }

    private void ValidateCsvContent(byte[] buffer, int bytesRead, FileValidationResult result)
    {
        try
        {
            var content = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            
            // Basic CSV validation - check for common delimiters
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 0)
            {
                var firstLine = lines[0];
                var commaCount = firstLine.Count(c => c == ',');
                var semicolonCount = firstLine.Count(c => c == ';');
                
                if (commaCount == 0 && semicolonCount == 0)
                {
                    result.AddWarning("File may not be a valid CSV (no common delimiters found in first line)");
                }
                
                result.Properties["EstimatedColumns"] = Math.Max(commaCount, semicolonCount) + 1;
                result.Properties["HasHeader"] = !char.IsDigit(firstLine.FirstOrDefault());
            }
        }
        catch (Exception ex)
        {
            result.AddWarning($"CSV validation failed: {ex.Message}");
        }
    }

    private string GenerateUniqueFileName(string originalFileName)
    {
        var extension = Path.GetExtension(originalFileName);
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(originalFileName);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        
        return $"{nameWithoutExtension}_{timestamp}_{uniqueId}{extension}";
    }

    private string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}

/// <summary>
/// Configuration for file upload service
/// </summary>
public class FileUploadConfiguration
{
    public string BaseUploadDirectory { get; set; } = "uploads";
    public long MaxFileSizeBytes { get; set; } = 50 * 1024 * 1024; // 50MB
    public int MaxFilesPerBatch { get; set; } = 10;
    public bool EnableVirusScanning { get; set; } = false;
    public string[] AllowedExtensions { get; set; } = { ".csv", ".pdf" };
    public string[] AllowedContentTypes { get; set; } = { "text/csv", "application/csv", "application/pdf" };
}