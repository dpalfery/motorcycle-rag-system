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

    public async Task<ChatMessage> SendQueryAsync(string query, string conversationId, List<string> previousQueries)
    {
        // Mocking the request/response for now as the API contract isn't fully defined in code
        // and we need a concrete implementation for the interface.
        var request = new
        {
            Query = query,
            Context = new
            {
                SessionId = conversationId,
                PreviousQueries = previousQueries
            }
        };

        var response = await _retryPolicy.ExecuteAsync(() =>
            _httpClient.PostAsJsonAsync("/api/motorcycles/query", request));

        response.EnsureSuccessStatusCode();

        // Assume we get a response that maps to ChatMessage
        // This part would involve deserializing the actual API response and mapping it.
        // For MVP structure, we return a dummy or map if we had the DTOs.

        return new ChatMessage
        {
            Id = Guid.NewGuid().ToString(),
            ConversationId = conversationId,
            Sender = MessageSender.System,
            Content = "This is a placeholder response from the API client.",
            Timestamp = DateTime.UtcNow,
            Status = MessageStatus.Sent
        };
    }
}
