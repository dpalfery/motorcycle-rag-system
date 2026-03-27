using System.Text.Json.Serialization;

namespace MotorcycleRAG.Admin.Services.Dtos;

internal sealed class LocalGraphJobStartRequest
{
    [JsonPropertyName("upload_id")]
    public string UploadId { get; init; } = string.Empty;

    [JsonPropertyName("local_file_path")]
    public string LocalFilePath { get; init; } = string.Empty;
}
