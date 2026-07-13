namespace MotorcycleRag.WebUI.BFF.HealthChecks;

/// <summary>
/// Probes the configured Data Protection blob without exposing Azure SDK details to the health check.
/// </summary>
public interface IDataProtectionBlobProbe
{
    /// <summary>Determines whether the configured Data Protection blob exists.</summary>
    Task<bool> ExistsAsync(CancellationToken cancellationToken);
}
