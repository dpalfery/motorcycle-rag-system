using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Domain.Models;

/// <summary>
/// Result of file upload operation
/// </summary>
public class FileUploadResult
{
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
    
    public Dictionary<string, object> Metadata { get; set; } = new();
}

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

/// <summary>
/// Configuration for processing schedules
/// </summary>
public class ProcessingScheduleConfig
{
    public bool IsEnabled { get; set; } = true;
    
    public string CronExpression { get; set; } = "0 0 2 * * *"; // Daily at 2 AM
    
    public TimeSpan ProcessingWindow { get; set; } = TimeSpan.FromHours(4);
    
    public int MaxConcurrentJobs { get; set; } = 3;
    
    public string ProcessingDirectory { get; set; } = "scheduled";
    
    public bool ProcessPendingUploads { get; set; } = true;
    
    public Dictionary<string, object> CustomSettings { get; set; } = new();
}

/// <summary>
/// Scheduled processing statistics
/// </summary>
public class ScheduledProcessingStats
{
    public DateTime? LastExecutionTime { get; set; }
    
    public DateTime? NextExecutionTime { get; set; }
    
    public PipelineStatus LastExecutionStatus { get; set; }
    
    public int TotalScheduledRuns { get; set; }
    
    public int SuccessfulRuns { get; set; }
    
    public int FailedRuns { get; set; }
    
    public TimeSpan AverageProcessingTime { get; set; }
    
    public int FilesProcessedInLastRun { get; set; }
    
    public string LastErrorMessage { get; set; } = string.Empty;
    
    public Dictionary<string, object> AdditionalStats { get; set; } = new();
}