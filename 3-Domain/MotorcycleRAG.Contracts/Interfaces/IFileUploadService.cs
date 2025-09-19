using Microsoft.AspNetCore.Http;
using MotorcycleRAG.Domain.Models;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Interface for handling secure file uploads with validation
/// </summary>
public interface IFileUploadService
{
    /// <summary>
    /// Upload and validate a single file
    /// </summary>
    Task<FileUploadResult> UploadFileAsync(IFormFile file, FileUploadOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upload and validate multiple files
    /// </summary>
    Task<BatchFileUploadResult> UploadFilesAsync(IEnumerable<IFormFile> files, FileUploadOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate file without uploading
    /// </summary>
    Task<FileValidationResult> ValidateFileAsync(IFormFile file, FileUploadOptions options);

    /// <summary>
    /// Get supported file types and constraints
    /// </summary>
    FileUploadConstraints GetUploadConstraints();

    /// <summary>
    /// Delete uploaded file
    /// </summary>
    Task<bool> DeleteFileAsync(string fileId);
}