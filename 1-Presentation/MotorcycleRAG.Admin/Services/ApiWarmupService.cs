using Microsoft.Extensions.Logging;

namespace MotorcycleRAG.Admin.Services;

/// <summary>
/// Wakes the remote API container app by issuing an unauthenticated health request.
/// This is safe to call multiple times; repeated calls dedupe per configured API host.
/// </summary>
internal sealed class ApiWarmupService : IApiWarmupService
{
    internal const string HttpClientName = "AdminApiWarmup";
    private static readonly TimeSpan WarmUpTimeout = TimeSpan.FromSeconds(90);

    private readonly HttpClient _httpClient;
    private readonly IConfigurationStateService _configurationStateService;
    private readonly ILogger<ApiWarmupService> _logger;
    private readonly SemaphoreSlim _warmUpSemaphore = new(1, 1);
    private string? _lastSuccessfulWarmUpTarget;

    public ApiWarmupService(
        IHttpClientFactory httpClientFactory,
        IConfigurationStateService configurationStateService,
        ILogger<ApiWarmupService> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        _configurationStateService = configurationStateService ?? throw new ArgumentNullException(nameof(configurationStateService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = httpClientFactory.CreateClient(HttpClientName);
    }

    public async Task WarmUpAsync()
    {
        var healthUri = ResolveHealthUri();
        if (healthUri is null)
        {
            return;
        }

        var warmUpTarget = healthUri.GetLeftPart(UriPartial.Path);
        if (string.Equals(_lastSuccessfulWarmUpTarget, warmUpTarget, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!await _warmUpSemaphore.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            if (string.Equals(_lastSuccessfulWarmUpTarget, warmUpTarget, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            using var cancellationTokenSource = new CancellationTokenSource(WarmUpTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, healthUri);

            _logger.LogInformation("Warming admin API at {HealthUri}", healthUri);

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationTokenSource.Token).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                _lastSuccessfulWarmUpTarget = warmUpTarget;
                _logger.LogInformation("Admin API warm-up completed for {ApiHost}", healthUri.Host);
                return;
            }

            _logger.LogWarning(
                "Admin API warm-up returned status code {StatusCode} for {HealthUri}",
                response.StatusCode,
                healthUri);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "Admin API warm-up timed out for {HealthUri}", healthUri);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Admin API warm-up failed for {HealthUri}", healthUri);
        }
        finally
        {
            _warmUpSemaphore.Release();
        }
    }

    private Uri? ResolveHealthUri()
    {
        var apiBaseUrl = _configurationStateService.ApiBaseUrl;
        if (!_configurationStateService.IsApiConfigured || apiBaseUrl is null)
        {
            return null;
        }

        return new Uri(apiBaseUrl, "health");
    }
}
