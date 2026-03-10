using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.Persistence.HealthChecks;

/// <summary>
/// Health check for Azure AI Search service endpoint availability
/// </summary>
public class AzureSearchHealthCheck : IHealthCheck
{
    private readonly AzureFoundryOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AzureSearchHealthCheck> _logger;

    public AzureSearchHealthCheck(
        IOptions<AzureFoundryOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<AzureSearchHealthCheck> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_options.SearchServiceEndpoint))
            {
                return HealthCheckResult.Unhealthy("Azure Search endpoint is not configured");
            }

            if (!Uri.TryCreate(_options.SearchServiceEndpoint, UriKind.Absolute, out var uri))
            {
                return HealthCheckResult.Unhealthy("Azure Search endpoint is not a valid URL");
            }

            var startTime = DateTime.UtcNow;
            
            // Test connectivity with a HEAD request to minimize data transfer
            using (var httpClient = _httpClientFactory.CreateClient())
            {
                httpClient.Timeout = TimeSpan.FromSeconds(5);
                using (var request = new HttpRequestMessage(HttpMethod.Head, uri))
                {
                    var response = await httpClient.SendAsync(request, cancellationToken);
                    if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.Forbidden)
                    {
                        _logger.LogWarning("Azure Search endpoint returned status code {StatusCode}", response.StatusCode);
                        return HealthCheckResult.Degraded($"Azure Search endpoint returned {response.StatusCode}");
                    }
                }
            }

            var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogInformation("Azure Search health check passed in {Duration}ms", duration);
            
            return HealthCheckResult.Healthy("Azure Search is healthy",
                new Dictionary<string, object> { { "response_time_ms", duration } });
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "Azure Search health check timed out");
            return HealthCheckResult.Unhealthy("Azure Search health check timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure Search health check failed");
            return HealthCheckResult.Unhealthy($"Azure Search health check failed: {ex.Message}", ex);
        }
    }
}
