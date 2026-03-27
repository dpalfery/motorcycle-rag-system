using MotorcycleRAG.Admin.Services.Dtos;

namespace MotorcycleRAG.Admin.Services;

internal interface ILocalProcessorService
{
    Task<LocalProcessorHealthResponse?> GetHealthAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LocalProcessorJobResponse>> GetJobsAsync(CancellationToken cancellationToken = default);

    Task<LocalProcessorHealthResponse> StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task<LocalGraphJobStartResult> StartBikeGraphJobAsync(string localFilePath, CancellationToken cancellationToken = default);
}
