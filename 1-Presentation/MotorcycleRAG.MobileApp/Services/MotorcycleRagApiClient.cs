using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.MobileApp.Exceptions;
using MotorcycleRAG.MobileApp.Models;
using Polly;
using Polly.Retry;

namespace MotorcycleRAG.MobileApp.Services;

public class MotorcycleRagApiClient : IApiClient
{
    private readonly HttpClient _httpClient;
    private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;
    private readonly ILogger<MotorcycleRagApiClient> _logger;

    public MotorcycleRagApiClient(HttpClient httpClient, IConfiguration configuration, ILogger<MotorcycleRagApiClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Get base URL from environment variable first, then configuration
        var baseUrl = Environment.GetEnvironmentVariable("MCR_MOBILE_API_BASE_URL")
            ?? configuration["ApiSettings:BaseUrl"];

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException(
                "API base URL is not configured. " +
                "Set MCR_MOBILE_API_BASE_URL environment variable or configure ApiSettings:BaseUrl in appsettings.json");
        }

        // Validate HTTPS
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https")
        {
            throw new InvalidOperationException(
                $"API base URL must use HTTPS protocol for security. " +
                $"Configured URL: {baseUrl}. " +
                $"Update ApiSettings:BaseUrl in appsettings.json to a valid HTTPS endpoint or set MCR_MOBILE_API_BASE_URL.");
        }

        _httpClient.BaseAddress = uri;
        _httpClient.Timeout = TimeSpan.FromSeconds(30);

        _logger.LogInformation("Mobile API client configured for {ApiHost}", uri.Host);

        _retryPolicy = Policy
            .HandleResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode && r.StatusCode != HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
    }

    public async Task<QueryResponse> QueryAsync(QueryRequest request)
    {
        var correlationId = Guid.NewGuid().ToString();
        _logger.LogInformation("Sending Query Request. CorrelationId: {CorrelationId}", correlationId);

        try
        {
            var response = await _retryPolicy.ExecuteAsync(() =>
                _httpClient.PostAsJsonAsync("/api/motorcycles/query", request));

            _logger.LogInformation("Received Query Response. Status: {StatusCode}, CorrelationId: {CorrelationId}", response.StatusCode, correlationId);

            await HandleErrorResponse(response);

            var queryResponse = await response.Content.ReadFromJsonAsync<QueryResponse>();
            return queryResponse ?? new QueryResponse();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in QueryAsync. CorrelationId: {CorrelationId}", correlationId);
            throw;
        }
    }

    public async Task<UserProfile> GetUserProfileAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        _logger.LogInformation("Sending GetUserProfile Request. CorrelationId: {CorrelationId}", correlationId);

        try
        {
            var response = await _retryPolicy.ExecuteAsync(() =>
                _httpClient.GetAsync(new Uri("/api/me", UriKind.Relative)));

            _logger.LogInformation("Received GetUserProfile Response. Status: {StatusCode}, CorrelationId: {CorrelationId}", response.StatusCode, correlationId);

            await HandleErrorResponse(response);

            var profile = await response.Content.ReadFromJsonAsync<UserProfile>();
            return profile ?? new UserProfile();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetUserProfileAsync. CorrelationId: {CorrelationId}", correlationId);
            throw;
        }
    }

    private async Task HandleErrorResponse(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Date ?? DateTime.UtcNow.AddMinutes(1);
            throw new RateLimitException("Daily request limit reached.", retryAfter.DateTime);
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new AuthenticationException("Authentication failed.");
        }

        throw new ApiException($"API request failed with status code {response.StatusCode}");
    }
}
