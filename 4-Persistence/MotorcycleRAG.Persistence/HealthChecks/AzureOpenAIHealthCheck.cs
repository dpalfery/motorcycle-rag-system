using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.Persistence.HealthChecks;

/// <summary>
/// Health check for Azure OpenAI service endpoint availability
/// </summary>
public class AzureOpenAIHealthCheck : IHealthCheck
{
    private readonly AzureAIOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AzureOpenAIHealthCheck> _logger;

    public AzureOpenAIHealthCheck(
        IOptions<AzureAIOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<AzureOpenAIHealthCheck> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_options.OpenAIEndpoint))
            {
                return HealthCheckResult.Unhealthy("Azure OpenAI endpoint is not configured");
            }

            if (!Uri.TryCreate(_options.OpenAIEndpoint, UriKind.Absolute, out var uri))
            {
                return HealthCheckResult.Unhealthy("Azure OpenAI endpoint is not a valid URL");
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
                        _logger.LogWarning("Azure OpenAI endpoint returned status code {StatusCode}", response.StatusCode);
                        return HealthCheckResult.Degraded($"Azure OpenAI endpoint returned {response.StatusCode}");
                    }
                }
            }

            var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogInformation("Azure OpenAI health check passed in {Duration}ms", duration);
            
            return HealthCheckResult.Healthy("Azure OpenAI is healthy", 
                new Dictionary<string, object> { { "response_time_ms", duration } });
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "Azure OpenAI health check timed out");
            return HealthCheckResult.Unhealthy("Azure OpenAI health check timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure OpenAI health check failed");
            return HealthCheckResult.Unhealthy($"Azure OpenAI health check failed: {ex.Message}", ex);
        }
    }
}
