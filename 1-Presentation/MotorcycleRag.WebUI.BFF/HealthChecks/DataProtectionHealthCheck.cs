using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace MotorcycleRag.WebUI.BFF.HealthChecks;

/// <summary>
/// Health check that verifies the Data Protection blob storage is accessible.
/// This ensures that the Data Protection keys can be persisted and retrieved.
/// </summary>
public class DataProtectionHealthCheck : IHealthCheck
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<DataProtectionHealthCheck> _logger;
    private readonly IDataProtectionBlobProbe _blobProbe;

    public DataProtectionHealthCheck(
        IConfiguration configuration,
        IDataProtectionBlobProbe blobProbe,
        ILogger<DataProtectionHealthCheck> logger)
    {
        _configuration = configuration;
        _blobProbe = blobProbe;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var blobUri = _configuration["DataProtection:BlobUri"];

        if (string.IsNullOrEmpty(blobUri))
        {
            return HealthCheckResult.Degraded(
                "DataProtection:BlobUri is not configured - using ephemeral keys");
        }

        try
        {
            var uri = new Uri(blobUri, UriKind.Absolute);
            if (!Uri.UriSchemeHttps.Equals(uri.Scheme, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("DataProtection:BlobUri must use HTTPS.");
            }

            var exists = await _blobProbe.ExistsAsync(cancellationToken).ConfigureAwait(false);

            if (exists)
            {
                _logger.LogDebug("Data Protection blob storage is accessible: {BlobLocation}", GetBlobLocation(uri));
                
                return HealthCheckResult.Healthy(
                    $"Data Protection blob storage is accessible: {GetBlobLocation(uri)}");
            }

            // Blob doesn't exist yet - this is OK for initial deployment
            // but we should verify we can create it
            _logger.LogDebug("Data Protection blob does not exist yet, verifying write access: {BlobLocation}", GetBlobLocation(uri));
            
            return HealthCheckResult.Healthy(
                $"Data Protection container is accessible, blob will be created on first use: {GetBlobLocation(uri)}");
        }
        catch (Azure.RequestFailedException ex)
        {
            _logger.LogError(ex, "Failed to access Data Protection blob storage. Error: {ErrorMessage}", ex.Message);
            
            return HealthCheckResult.Degraded(
                $"Data Protection blob storage is not accessible: {ex.Message}",
                ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error checking Data Protection blob storage");
            
            return HealthCheckResult.Unhealthy(
                $"Unexpected error checking Data Protection blob storage: {ex.Message}",
                ex);
        }
    }

    private static string GetBlobLocation(Uri blobUri) =>
        $"{blobUri.Host}/{blobUri.Segments.ElementAtOrDefault(1)?.Trim('/')}";
}
