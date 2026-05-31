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
    private readonly IOptions<AzureFoundryOptions> _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AzureOpenAIHealthCheck> _logger;

    public AzureOpenAIHealthCheck(
        IOptions<AzureFoundryOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<AzureOpenAIHealthCheck> logger)
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
            _logger.LogWarning(ex, "Azure OpenAI is not configured for this environment");
            return HealthCheckResult.Degraded("Azure OpenAI is not configured for this environment");
        }

        try
        {
            if (string.IsNullOrWhiteSpace(resolvedOptions.FoundryEndpoint))
            {
                return HealthCheckResult.Unhealthy("Azure Foundry endpoint is not configured");
            }

            if (!Uri.TryCreate(resolvedOptions.FoundryEndpoint, UriKind.Absolute, out var uri))
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
                        _logger.LogWarning("Azure Foundry endpoint returned status code {StatusCode}", response.StatusCode);
                        return HealthCheckResult.Degraded($"Azure Foundry endpoint returned {response.StatusCode}");
                    }
                }
            }

            var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogInformation("Azure Foundry health check passed in {Duration}ms", duration);

            return HealthCheckResult.Healthy("Azure Foundry is healthy",
                new Dictionary<string, object> { { "response_time_ms", duration } });
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "Azure Foundry health check timed out");
            return HealthCheckResult.Unhealthy("Azure Foundry health check timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure Foundry health check failed");
            return HealthCheckResult.Unhealthy($"Azure Foundry health check failed: {ex.Message}", ex);
        }
    }
}
