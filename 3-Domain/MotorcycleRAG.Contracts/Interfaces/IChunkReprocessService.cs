namespace MotorcycleRAG.Contracts.Interfaces;

using MotorcycleRAG.Contracts.Models.DTOs;

public interface IChunkReprocessService
{
    Task<ReprocessResultDto> ReprocessByJobIdAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task<ReprocessResultDto> ReprocessAllNotSucceededAsync(CancellationToken cancellationToken = default);
    Task<ReprocessResultDto> ReprocessAllAsync(CancellationToken cancellationToken = default);
}
