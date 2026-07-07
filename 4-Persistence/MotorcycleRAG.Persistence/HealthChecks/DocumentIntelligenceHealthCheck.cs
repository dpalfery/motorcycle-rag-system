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
    private readonly IOptions<AzureFoundryOptions> _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DocumentIntelligenceHealthCheck> _logger;

    public DocumentIntelligenceHealthCheck(
        IOptions<AzureFoundryOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<DocumentIntelligenceHealthCheck> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        AzureFoundryOptions resolvedOptions;
        try
        {
            resolvedOptions = _options.Value;
        }
        catch (OptionsValidationException ex)
        {
            _logger.LogWarning(ex, "Document Intelligence is not configured for this environment");
            return HealthCheckResult.Degraded("Document Intelligence is not configured for this environment");
        }

        try
        {
            if (string.IsNullOrWhiteSpace(resolvedOptions.DocumentIntelligenceEndpoint))
            {
                return HealthCheckResult.Degraded("Document Intelligence is not configured for this environment");
            }

            if (!Uri.TryCreate(resolvedOptions.DocumentIntelligenceEndpoint, UriKind.Absolute, out var uri))
            {
                return HealthCheckResult.Degraded("Document Intelligence endpoint is not a valid URL");
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
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "Document Intelligence health check timed out");
            return HealthCheckResult.Unhealthy("Document Intelligence health check timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Document Intelligence health check failed");
            return HealthCheckResult.Unhealthy($"Document Intelligence health check failed: {ex.Message}", ex);
        }
    }
}
