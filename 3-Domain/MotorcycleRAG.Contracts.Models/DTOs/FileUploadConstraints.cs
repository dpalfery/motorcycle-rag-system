using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MotorcycleRAG.Domain.DTOs;

/// <summary>
/// File upload constraints information
/// </summary>
public class FileUploadConstraints {
    public long MaxFileSizeBytes { get; set; }

    public string MaxFileSizeDisplay { get; set; } = string.Empty;

    public Collection<string> SupportedFileTypes { get; } = new();

    public Collection<string> SupportedExtensions { get; } = new();

    public int MaxFilesPerBatch { get; set; } = 10;

    public Dictionary<string, string> FileTypeDescriptions { get; } = new();
}
