using Microsoft.Extensions.Caching.Memory;
using MotorcycleRAG.Contracts.Interfaces;

namespace MotorcycleRAG.Application.Services.Ingestion;

/// <inheritdoc />
public sealed class IngestionSourceAccessTokenService : IIngestionSourceAccessTokenService
{
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(2);
    private readonly IMemoryCache _cache;

    public IngestionSourceAccessTokenService(IMemoryCache cache)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    /// <inheritdoc />
    public string CreateToken(string uploadId, string documentType, TimeSpan? lifetime = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uploadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentType);

        var token = Guid.NewGuid().ToString("N");
        var grant = new IngestionSourceAccessGrant(
            uploadId.Trim(),
            documentType.Trim().ToLowerInvariant());

        _cache.Set(
            BuildCacheKey(token),
            grant,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = lifetime ?? DefaultLifetime,
                Size = 1,
            });

        return token;
    }

    /// <inheritdoc />
    public bool IsValid(string token, string uploadId, string documentType)
    {
        if (string.IsNullOrWhiteSpace(token)
            || string.IsNullOrWhiteSpace(uploadId)
            || string.IsNullOrWhiteSpace(documentType)) {
            return false;
        }

        if (!_cache.TryGetValue(BuildCacheKey(token), out IngestionSourceAccessGrant? grant)
            || grant is null) {
            return false;
        }

        return string.Equals(grant.UploadId, uploadId.Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(grant.DocumentType, documentType.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildCacheKey(string token) => $"ingestion-source-access:{token}";

    private sealed record IngestionSourceAccessGrant(string UploadId, string DocumentType);
}
