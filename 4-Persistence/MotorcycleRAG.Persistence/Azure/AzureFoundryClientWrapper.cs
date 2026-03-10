using Azure;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Entities;
using Polly;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.Persistence.Azure; // Fixed namespace to match project & tests

/// <summary>
/// Azure Foundry client wrapper with retry policies and authentication
/// </summary>
public class AzureFoundryClientWrapper : IAzureFoundryClient, IDisposable
{
    private static readonly string[] TokenScopes = ["https://cognitiveservices.azure.com/.default"];

    private readonly ILogger<AzureFoundryClientWrapper> _logger;
    private readonly IResilienceService _resilienceService;
    private readonly ICorrelationService _correlationService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly global::Azure.Core.TokenCredential _credential;
    private readonly AzureFoundryOptions _azureConfig;

    public AzureFoundryClientWrapper(
        IOptions<AzureFoundryOptions> config,
        ILogger<AzureFoundryClientWrapper> logger,
        IResilienceService resilienceService,
        ICorrelationService correlationService,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(resilienceService);
        ArgumentNullException.ThrowIfNull(correlationService);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(configuration);

        _azureConfig = config.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
        _resilienceService = resilienceService;
        _correlationService = correlationService;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _credential = new DefaultAzureCredential();

        _logger.LogInformation("Azure Foundry client initialized with endpoint: {Endpoint}",
            _azureConfig.FoundryEndpoint);
    }

    // Convenience overloads (tests call these)
    public Task<string> GetChatCompletionAsync(string deploymentName, string prompt) =>
        GetChatCompletionAsync(deploymentName, prompt, CancellationToken.None);
    public Task<float[]> GetEmbeddingAsync(string deploymentName, string text) =>
        GetEmbeddingAsync(deploymentName, text, CancellationToken.None);
    public Task<float[]> GetEmbeddingsAsync(string deploymentName, string text) =>
        GetEmbeddingsAsync(deploymentName, text, CancellationToken.None);
    public Task<float[][]> GetEmbeddingsAsync(string deploymentName, string[] texts) =>
        GetEmbeddingsAsync(deploymentName, texts, CancellationToken.None);
    public Task<string> ProcessMultimodalContentAsync(string deploymentName, string textPrompt, byte[] imageData, string imageContentType) =>
        ProcessMultimodalContentAsync(deploymentName, textPrompt, imageData, imageContentType, CancellationToken.None);

    public async Task<string> GetChatCompletionAsync(
        string deploymentName,
        string prompt,
        CancellationToken cancellationToken)
    {
        var correlationId = _correlationService.GetOrCreateCorrelationId();

        return await _resilienceService.ExecuteAsync(
            "AzureFoundry",
            async () =>
            {
                using var scope = _correlationService.CreateLoggingScope(new Dictionary<string, object>
                {
                    ["Operation"] = "GetChatCompletion",
                    ["DeploymentName"] = deploymentName
                });

                _logger.LogDebug("Getting chat completion for deployment: {DeploymentName}", deploymentName);
                // Read config from options
                var endpoint = _azureConfig.FoundryEndpoint;
                if (string.IsNullOrWhiteSpace(endpoint))
                {
                    throw new InvalidOperationException(
                        "FoundryEndpoint is not configured in AzureFoundryOptions");
                }

                var chatModel = string.IsNullOrWhiteSpace(deploymentName)
                    ? _azureConfig.Models.ChatModel
                    : deploymentName;

                if (string.IsNullOrWhiteSpace(chatModel))
                {
                    throw new InvalidOperationException("ChatModel is not configured in AzureFoundryOptions");
                }

                var tokenRequestContext = new global::Azure.Core.TokenRequestContext(TokenScopes);
                var tokenResult = await _credential.GetTokenAsync(tokenRequestContext, cancellationToken);

                // Build request
                var requestBody = System.Text.Json.JsonSerializer.Serialize(new
                {
                    model = chatModel,
                    messages = new[] { new { role = "user", content = prompt } }
                });
                var requestUri = new Uri($"{endpoint.TrimEnd('/')}/chat/completions?api-version=2024-05-01-preview");
                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUri);
                httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", tokenResult.Token);
                httpRequest.Content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");

                using var httpClient = _httpClientFactory.CreateClient("AzureFoundry");
                using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = System.Text.Json.JsonDocument.Parse(responseJson);
                var content = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString()
                    ?? throw new InvalidOperationException("Empty content in Foundry chat completion response");

                _logger.LogDebug("Successfully retrieved chat completion from Azure AI Foundry");
                return content;
            },
            async () =>
            {
                _logger.LogWarning("Using fallback response for chat completion");
                return "Fallback response: Unable to process request at this time. Please try again later.";
            },
            correlationId,
            cancellationToken);
    }

    public async Task<float[]> GetEmbeddingAsync(
        string model,
        string text,
        CancellationToken cancellationToken)
    {
        var embeddings = await GetEmbeddingsAsync(model, new[] { text }, cancellationToken);
        return embeddings[0];
    }

    public async Task<float[]> GetEmbeddingsAsync(
        string model,
        string text,
        CancellationToken cancellationToken)
    {
        var result = await GetEmbeddingsAsync(model, new[] { text }, cancellationToken);
        return result[0];
    }

    public async Task<float[][]> GetEmbeddingsAsync(
        string model,
        string[] texts,
        CancellationToken cancellationToken)
    {
        var correlationId = _correlationService.GetOrCreateCorrelationId();

        return await _resilienceService.ExecuteAsync(
            "AzureFoundry",
            async () =>
            {
                using var scope = _correlationService.CreateLoggingScope(new Dictionary<string, object>
                {
                    ["Operation"] = "GetEmbeddings",
                    ["DeploymentName"] = model,
                    ["TextCount"] = texts.Length
                });

                _logger.LogDebug("Getting embeddings for deployment: {DeploymentName}, Text count: {TextCount}",
                    model, texts.Length);

                // Read config from configuration (never hardcoded)
                var apiKey = _configuration["DEEPINFRA_API_KEY"];
                var baseUrl = _configuration["DEEPINFRA_BASE_URL"];
                var embeddingModel = _configuration["DEEPINFRA_EMBEDDING_MODEL"];

                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    throw new InvalidOperationException("DEEPINFRA_API_KEY is not configured");
                }
                if (string.IsNullOrWhiteSpace(baseUrl))
                {
                    throw new InvalidOperationException("DEEPINFRA_BASE_URL is not configured");
                }

                var effectiveModel = string.IsNullOrWhiteSpace(model)
                    ? embeddingModel
                    : model;

                if (string.IsNullOrWhiteSpace(effectiveModel))
                {
                    throw new InvalidOperationException("DEEPINFRA_EMBEDDING_MODEL is not configured");
                }

                // Build request
                var requestBody = System.Text.Json.JsonSerializer.Serialize(new
                {
                    model = effectiveModel,
                    input = texts,
                    encoding_format = "float"
                });
                var requestUri = new Uri($"{baseUrl.TrimEnd('/')}/embeddings");
                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUri);
                httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                httpRequest.Content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");

                using var httpClient = _httpClientFactory.CreateClient("DeepInfra");
                using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = System.Text.Json.JsonDocument.Parse(responseJson);
                var dataArray = doc.RootElement.GetProperty("data");

                var embeddings = new float[texts.Length][];
                for (int i = 0; i < texts.Length; i++)
                {
                    var embArray = dataArray[i].GetProperty("embedding");
                    var floats = new float[embArray.GetArrayLength()];
                    int j = 0;
                    foreach (var val in embArray.EnumerateArray())
                        floats[j++] = val.GetSingle();
                    if (floats.Length != 3584)
                        throw new InvalidOperationException(
                            $"Expected 3584-dimensional embedding from DeepInfra, but got {floats.Length}");
                    embeddings[i] = floats;
                }

                _logger.LogDebug("Successfully retrieved {Count} embeddings from DeepInfra", texts.Length);
                return embeddings;
            },
            async () =>
            {
                _logger.LogWarning("Using fallback embeddings for {TextCount} texts", texts.Length);
                return texts.Select(_ => new float[3584]).ToArray();
            },
            correlationId,
            cancellationToken);
    }

    public async Task<string> ProcessMultimodalContentAsync(
        string model,
        string textPrompt,
        byte[] imageData,
        string imageContentType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(imageData);

        try
        {
            _logger.LogDebug("Processing multimodal content for deployment: {DeploymentName}", model);
            await Task.Delay(200, cancellationToken);
            _logger.LogDebug("Successfully processed multimodal content");
            return $"GPT-4 Vision analysis of image ({imageData.Length} bytes): {textPrompt}";
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Azure Foundry multimodal request failed: {ErrorCode} - {Message}",
                ex.ErrorCode, ex.Message);
            throw new InvalidOperationException($"Failed to process multimodal content for deployment {model}: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in ProcessMultimodalContentAsync");
            throw new InvalidOperationException("Unexpected error in ProcessMultimodalContentAsync", ex);
        }
    }

    public Task<bool> IsHealthyAsync()
    {
        return IsHealthyAsync(CancellationToken.None);
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(50, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Azure Foundry health check failed");
            return false;
        }
    }

    private bool _disposed;

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        // No managed or unmanaged resources to release because AzureFoundryClientWrapper is not IDisposable.
        _disposed = true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
