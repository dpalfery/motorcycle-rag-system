using System.Collections.ObjectModel;
using System.Collections;

namespace MotorcycleRAG.Admin.Models.Api;

/// <summary>
/// Upload constraints information
/// </summary>
#pragma warning disable CA1812 // Instantiated via deserialization
#pragma warning disable S3059 // Public properties required for System.Text.Json serialization
internal class UploadConstraints
{
    public long MaxFileSizeBytes { get; set; }
#pragma warning restore S3059
    public int MaxFilesPerBatch { get; set; }
    public Collection<string> AllowedFileTypes { get; } = new();
}
