using System.Collections.ObjectModel;
using System.Collections;

namespace MotorcycleRAG.Admin.Models.Api;

/// <summary>
/// Upload constraints information
/// </summary>
public class UploadConstraints
{
    public long MaxFileSizeBytes { get; set; }
    public int MaxFilesPerBatch { get; set; }
    public Collection<string> AllowedFileTypes { get; } = new();
}
