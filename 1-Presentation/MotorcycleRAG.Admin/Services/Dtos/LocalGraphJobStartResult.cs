namespace MotorcycleRAG.Admin.Services.Dtos;

internal sealed class LocalGraphJobStartResult
{
    public string UploadId { get; init; } = string.Empty;

    public string JobId { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;
}
