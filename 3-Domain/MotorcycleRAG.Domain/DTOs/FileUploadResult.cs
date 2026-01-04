using System;
using System.Collections.Generic;

namespace MotorcycleRAG.Domain.DTOs;

/// <summary>
/// Result of file upload operation
/// </summary>
public class FileUploadResult {
    public string FileId { get; set; } = Guid.NewGuid().ToString();

    public string OriginalFileName { get; set; } = string.Empty;

    public string StoredFileName { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    public long FileSize { get; set; }

    public string ContentType { get; set; } = string.Empty;

    public FileType DetectedFileType { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public FileValidationResult ValidationResult { get; set; } = new();

    public bool IsValid => ValidationResult.IsValid;

    public Dictionary<string, object> Metadata { get; } = new();
}
