namespace MotorcycleRAG.Admin.Services;

/// <summary>
/// Sends a lightweight startup request to wake the remote admin API before
/// the user reaches authenticated workflows.
/// </summary>
internal interface IApiWarmupService
{
    Task WarmUpAsync();
}
