using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Admin.Models.Api;

/// <summary>
/// Response wrapper for batch uploads with optional server-side processing.
/// </summary>
internal class BatchFileUploadResponseDto
{
    public BatchFileUploadResult Upload { get; set; } = new();
}
