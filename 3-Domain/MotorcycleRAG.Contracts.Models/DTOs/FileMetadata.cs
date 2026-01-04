namespace MotorcycleRAG.Domain.DTOs;

/// <summary>
/// Metadata for file upload operations
/// </summary>
public class FileMetadata {
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long ContentLength { get; set; }
}
