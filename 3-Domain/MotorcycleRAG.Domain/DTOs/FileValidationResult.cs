using System;
using System.Collections.Generic;

namespace MotorcycleRAG.Domain.DTOs;

/// <summary>
/// File validation result
/// </summary>
public class FileValidationResult
{
    public bool IsValid { get; set; } = true;

    public List<string> Errors { get; set; } = new();

    public List<string> Warnings { get; set; } = new();

    public FileType DetectedFileType { get; set; }

    public string ContentType { get; set; } = string.Empty;

    public long FileSize { get; set; }

    public Dictionary<string, object> Properties { get; set; } = new();

    public void AddError(string error)
    {
        Errors.Add(error);
        IsValid = false;
    }

    public void AddWarning(string warning)
    {
        Warnings.Add(warning);
    }
}
