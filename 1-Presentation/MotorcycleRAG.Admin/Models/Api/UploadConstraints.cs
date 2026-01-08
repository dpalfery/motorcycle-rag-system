using System.Collections.ObjectModel;
using System.Collections;

namespace MotorcycleRAG.Admin.Models.Api;

/// <summary>
/// Upload constraints information
/// </summary>
public class UploadConstraints
{
    internal long MaxFileSizeBytes { get; set; }
    internal int MaxFilesPerBatch { get; set; }
    internal Collection<string> AllowedFileTypes { get; } = new();
}
