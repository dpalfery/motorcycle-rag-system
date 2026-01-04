using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// File validation result
/// </summary>
public class FileValidationResult {
    public bool IsValid { get; set; } = true;

    public Collection<string> Errors { get; } = new();

    public Collection<string> Warnings { get; } = new();

    public FileType DetectedFileType { get; set; }

    public string ContentType { get; set; } = string.Empty;

    public long FileSize { get; set; }

    public Dictionary<string, object> Properties { get; } = new();

    public void AddError(string error) {
        Errors.Add(error);
        IsValid = false;
    }

    public void AddWarning(string warning) {
        Warnings.Add(warning);
    }
}

