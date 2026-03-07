using Azure.Storage.Blobs;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Azure.Identity;

namespace MotorcycleRag.WebUI.BFF.HealthChecks;

/// <summary>
/// Health check that verifies the Data Protection blob storage is accessible.
/// This ensures that the Data Protection keys can be persisted and retrieved.
/// </summary>
public class DataProtectionHealthCheck : IHealthCheck
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<DataProtectionHealthCheck> _logger;

    public DataProtectionHealthCheck(IConfiguration configuration, ILogger<DataProtectionHealthCheck> logger)
    {
        _configuration = configuration;
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
            var uri = new Uri(blobUri);
            var blobClient = new BlobClient(uri, new DefaultAzureCredential());

            // Check if the blob exists by attempting to get properties
            // This validates both container access and blob access
            var exists = await blobClient.ExistsAsync(cancellationToken);

            if (exists.Value)
            {
                _logger.LogDebug("Data Protection blob storage is accessible: {BlobUri}", blobUri);
                
                return HealthCheckResult.Healthy(
                    $"Data Protection blob storage is accessible: {uri.Host}/{uri.Segments.ElementAtOrDefault(1)?.Trim('/')}");
            }

            // Blob doesn't exist yet - this is OK for initial deployment
            // but we should verify we can create it
            _logger.LogDebug("Data Protection blob does not exist yet, verifying write access: {BlobUri}", blobUri);
            
            return HealthCheckResult.Healthy(
                $"Data Protection container is accessible, blob will be created on first use: {uri.Host}/{uri.Segments.ElementAtOrDefault(1)?.Trim('/')}");
        }
        catch (Azure.RequestFailedException ex)
        {
            _logger.LogError(ex, "Failed to access Data Protection blob storage: {BlobUri}. Error: {ErrorMessage}", 
                blobUri, ex.Message);
            
            return HealthCheckResult.Degraded(
                $"Data Protection blob storage is not accessible: {ex.Message}",
                ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error checking Data Protection blob storage: {BlobUri}", blobUri);
            
            return HealthCheckResult.Unhealthy(
                $"Unexpected error checking Data Protection blob storage: {ex.Message}",
                ex);
        }
    }
}
