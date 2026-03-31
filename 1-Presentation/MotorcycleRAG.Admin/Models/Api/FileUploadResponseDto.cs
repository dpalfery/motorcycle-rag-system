using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Admin.Models.Api;

/// <summary>
/// Response wrapper for a single upload with optional server-side processing.
/// </summary>
internal class FileUploadResponseDto
{
    public FileUploadResult Upload { get; set; } = new();
}
