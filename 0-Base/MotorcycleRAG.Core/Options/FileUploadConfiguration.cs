namespace MotorcycleRAG.Core.Options;

/// <summary>
/// Configuration for file upload service.
/// </summary>
public class FileUploadConfiguration {
    public string BaseUploadDirectory { get; set; } = "uploads";
    public long MaxFileSizeBytes { get; set; } = 50 * 1024 * 1024;
    public int MaxFilesPerBatch { get; set; } = 10;
    public bool EnableVirusScanning { get; set; }
    public IReadOnlyList<string> AllowedExtensions { get; init; } = new[] { ".CSV", ".PDF" };
    public IReadOnlyList<string> AllowedContentTypes { get; init; } = new[] { "text/csv", "application/csv", "application/pdf" };
}
