using System;
using System.Collections.Generic;

namespace MotorcycleRAG.Domain.DTOs;

/// <summary>
/// File upload options and constraints
/// </summary>
public class FileUploadOptions
{
    public long MaxFileSizeBytes { get; set; } = 50 * 1024 * 1024; // 50MB default

    public HashSet<string> AllowedContentTypes { get; set; } = new()
    {
        "application/pdf",
        "text/csv",
        "application/csv"
    };

    public HashSet<string> AllowedFileExtensions { get; set; } = new()
    {
        ".pdf",
        ".csv"
    };

    public bool ValidateFileContent { get; set; } = true;

    public bool ScanForViruses { get; set; } = false;

    public string UploadDirectory { get; set; } = "uploads";

    public bool GenerateUniqueFileName { get; set; } = true;

    public bool PreserveOriginalFileName { get; set; } = false;

    public Dictionary<string, object> CustomValidationRules { get; set; } = new();
}
