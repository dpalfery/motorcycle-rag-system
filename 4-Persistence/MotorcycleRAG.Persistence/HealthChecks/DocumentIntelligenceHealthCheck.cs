using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.Persistence.HealthChecks;

/// <summary>
/// Health check for Azure Document Intelligence service endpoint availability
/// </summary>
public class DocumentIntelligenceHealthCheck : IHealthCheck
{
    private readonly AzureAIOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DocumentIntelligenceHealthCheck> _logger;

    public DocumentIntelligenceHealthCheck(
        IOptions<AzureAIOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<DocumentIntelligenceHealthCheck> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_options.DocumentIntelligenceEndpoint))
            {
                return HealthCheckResult.Unhealthy("Document Intelligence endpoint is not configured");
            }

            if (!Uri.TryCreate(_options.DocumentIntelligenceEndpoint, UriKind.Absolute, out var uri))
            {
                return HealthCheckResult.Unhealthy("Document Intelligence endpoint is not a valid URL");
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
                        _logger.LogWarning("Document Intelligence endpoint returned status code {StatusCode}", response.StatusCode);
                        return HealthCheckResult.Degraded($"Document Intelligence endpoint returned {response.StatusCode}");
                    }
                }
            }

            var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogInformation("Document Intelligence health check passed in {Duration}ms", duration);
            
            return HealthCheckResult.Healthy("Document Intelligence is healthy",
                new Dictionary<string, object> { { "response_time_ms", duration } });
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Document Intelligence health check timed out");
            return HealthCheckResult.Unhealthy("Document Intelligence health check timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Document Intelligence health check failed");
            return HealthCheckResult.Unhealthy($"Document Intelligence health check failed: {ex.Message}", ex);
        }
    }
}
