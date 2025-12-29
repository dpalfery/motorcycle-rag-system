using System;
using System.Collections.Generic;

namespace MotorcycleRAG.Domain.DTOs;

/// <summary>
/// Batch file upload result
/// </summary>
public class BatchFileUploadResult
{
    public string BatchId { get; set; } = Guid.NewGuid().ToString();

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public int TotalFiles { get; set; }

    public int SuccessfulUploads { get; set; }

    public int FailedUploads { get; set; }

    public List<FileUploadResult> Results { get; set; } = new();

    public List<string> Errors { get; set; } = new();

    public bool AllFilesUploaded => FailedUploads == 0;
}
