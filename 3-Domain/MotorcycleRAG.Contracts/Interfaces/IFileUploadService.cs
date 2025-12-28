using MotorcycleRAG.Contracts.Models;
using MotorcycleRAG.Domain.Models;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Metadata for file upload operations
/// </summary>
public class FileMetadata
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long ContentLength { get; set; }
}

/// <summary>
/// Interface for handling secure file uploads with validation
/// </summary>
public interface IFileUploadService
{
    /// <summary>
    /// Upload and validate a single file
    /// </summary>
    Task<FileUploadResult> UploadFileAsync(Stream fileStream, FileMetadata metadata, FileUploadOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upload and validate multiple files
    /// </summary>
    Task<BatchFileUploadResult> UploadFilesAsync(IEnumerable<(Stream stream, FileMetadata metadata)> files, FileUploadOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate file without uploading
    /// </summary>
    Task<FileValidationResult> ValidateFileAsync(Stream fileStream, FileMetadata metadata, FileUploadOptions options);

    /// <summary>
    /// Get supported file types and constraints
    /// </summary>
    FileUploadConstraints GetUploadConstraints();

    /// <summary>
    /// Delete uploaded file
    /// </summary>
    Task<bool> DeleteFileAsync(string fileId);
}
