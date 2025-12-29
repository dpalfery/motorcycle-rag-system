using System;
using System.Collections.Generic;

namespace MotorcycleRAG.Domain.DTOs;

/// <summary>
/// File upload constraints information
/// </summary>
public class FileUploadConstraints
{
    public long MaxFileSizeBytes { get; set; }

    public string MaxFileSizeDisplay { get; set; } = string.Empty;

    public List<string> SupportedFileTypes { get; set; } = new();

    public List<string> SupportedExtensions { get; set; } = new();

    public int MaxFilesPerBatch { get; set; } = 10;

    public Dictionary<string, string> FileTypeDescriptions { get; set; } = new();
}
