using MotorcycleRAG.Admin.Services.Dtos;

namespace MotorcycleRAG.Admin.Services;

internal interface ILocalProcessorService
{
    IReadOnlyList<string> RecentProcessOutput { get; }

    Task<LocalProcessorHealthResponse?> GetHealthAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LocalProcessorJobResponse>> GetJobsAsync(CancellationToken cancellationToken = default);

    Task<int> ClearFinishedJobsAsync(CancellationToken cancellationToken = default);

    Task<LocalProcessorHealthResponse> StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task<LocalGraphJobStartResult> StartBikeGraphJobAsync(string localFilePath, CancellationToken cancellationToken = default);
}
