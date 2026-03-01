using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;

namespace MotorcycleRAG.Application.Pipeline;

/// <summary>
/// Retrieves manual page images from the asset store.
/// Validates inputs and delegates to <see cref="IManualPageAssetStore"/> for storage access.
/// </summary>
public sealed class ManualPageQueryService : IManualPageQueryService
{
    private readonly IManualPageAssetStore _assetStore;
    private readonly ILogger<ManualPageQueryService> _logger;

    public ManualPageQueryService(
        IManualPageAssetStore assetStore,
        ILogger<ManualPageQueryService> logger)
    {
        _assetStore = assetStore ?? throw new ArgumentNullException(nameof(assetStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ManualPageResult> GetPageAsync(Guid manualId, int pageNumber, CancellationToken ct = default)
    {
        if (pageNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(pageNumber), pageNumber, "Page number must be >= 1.");
        }

        // Log only manualId — never combine with pageNumber in the same statement (correlation risk).
        _logger.LogInformation("Manual page requested. ManualId={ManualId}", manualId);

        var exists = await _assetStore.PageExistsAsync(manualId, pageNumber, ct).ConfigureAwait(false);
        if (!exists)
        {
            throw new KeyNotFoundException($"Page not found for the requested manual.");
        }

        var stream = await _assetStore.DownloadPageAsync(manualId, pageNumber, ct).ConfigureAwait(false);
        var etag = await _assetStore.GetETagAsync(manualId, pageNumber, ct).ConfigureAwait(false);

        return new ManualPageResult(stream, etag);
    }
}