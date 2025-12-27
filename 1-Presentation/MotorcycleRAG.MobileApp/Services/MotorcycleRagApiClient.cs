using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using MotorcycleRAG.MobileApp.Models;
using Polly;
using Polly.Retry;

namespace MotorcycleRAG.MobileApp.Services;

public class MotorcycleRagApiClient : IApiClient
{
    private readonly HttpClient _httpClient;
    private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;

    public MotorcycleRagApiClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        var baseUrl = configuration["ApiSettings:BaseUrl"] ?? "https://api.motorcyclerag.com";
        _httpClient.BaseAddress = new Uri(baseUrl);

        _retryPolicy = Policy
            .HandleResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
    }

    public async Task<QueryResponse> QueryAsync(QueryRequest request)
    {
        var response = await _retryPolicy.ExecuteAsync(() =>
            _httpClient.PostAsJsonAsync("/api/motorcycles/query", request));

        response.EnsureSuccessStatusCode();

        var queryResponse = await response.Content.ReadFromJsonAsync<QueryResponse>();
        return queryResponse ?? new QueryResponse();
    }
}
