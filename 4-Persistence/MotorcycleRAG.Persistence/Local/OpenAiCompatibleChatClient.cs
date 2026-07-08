using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.Persistence.Local;

/// <summary>
/// Lightweight OpenAI-compatible chat-completion client for local LLM endpoints (e.g. LM Studio).
/// </summary>
/// <remarks>
/// &lt;para&gt;This is the first chat client in the .NET stack (OQ4): the existing &lt;c&gt;AzureFoundryClientWrapper&lt;/c&gt;
/// does embeddings + multimodal only, and the cloud chat path runs through the Foundry agents SDK.&lt;/para&gt;
/// &lt;para&gt;The client is HTTP-thin by design: it builds a &lt;c&gt;POST {Endpoint}/chat/completions&lt;/c&gt; request
/// with a single system + user turn, reads the first choice's content, and returns it. Prompt engineering
/// and output parsing belong to the caller (e.g. the &lt;c&gt;MotorcycleCategoryClassifier&lt;/c&gt; Application service).&lt;/para&gt;
/// &lt;para&gt;An optional bearer token is read from &lt;c&gt;Classifier:ApiKey&lt;/c&gt; — local LM Studio does not require
/// it, but the same client works against any authenticated OpenAI-compatible endpoint (R6: prod reachability).&lt;/para&gt;
/// &lt;para&gt;Transport/HTTP failures propagate as <see cref="HttpRequestException"/>; the caller applies a
/// defined fallback policy. The named <c>LocalChat</c> <see cref="HttpClient"/> is registered with a timeout
/// derived from <see cref="ClassifierOptions.TimeoutSeconds"/> at DI time.&lt;/para&gt;
/// </remarks>
public class OpenAiCompatibleChatClient : ILocalChatClient
{
    /// <summary>
    /// The logical name used to register the typed/named <see cref="HttpClient"/> in DI.
    /// The DI registration configures timeout and optional auth for this client name.
    /// </summary>
    public const string HttpClientName = "LocalChat";

    private const string JsonContentType = "application/json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<ClassifierOptions> _options;
    private readonly ILogger<OpenAiCompatibleChatClient> _logger;
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAiCompatibleChatClient"/>.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory (resolved via the <see cref="HttpClientName"/> name).</param>
    /// <param name="options">Classifier options (endpoint + model).</param>
    /// <param name="configuration">Configuration (used only to read the optional <c>Classifier:ApiKey</c>).</param>
    /// <param name="logger">Logger instance.</param>
    public OpenAiCompatibleChatClient(
        IHttpClientFactory httpClientFactory,
        IOptions<ClassifierOptions> options,
        IConfiguration configuration,
        ILogger<OpenAiCompatibleChatClient> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<string> GetChatCompletionAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);

        var opts = _options.Value;
        if (string.IsNullOrWhiteSpace(opts.Endpoint))
        {
            throw new InvalidOperationException(
                "Classifier:Endpoint is not configured.");
        }
        if (string.IsNullOrWhiteSpace(opts.Model))
        {
            throw new InvalidOperationException(
                "Classifier:Model is not configured.");
        }

        var requestUri = new Uri(opts.Endpoint.TrimEnd('/') + "/chat/completions", UriKind.Absolute);

        var requestBody = JsonSerializer.Serialize(new ChatCompletionRequest
        {
            Model = opts.Model,
            Temperature = 0.0,           // deterministic classification
            MaxTokens = 16,              // we only want a single category token back
            Messages =
            [
                new() { Role = "system", Content = systemPrompt },
                new() { Role = "user", Content = userPrompt }
            ]
        }, JsonOptions);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUri);
        httpRequest.Content = new StringContent(requestBody, Encoding.UTF8);
        httpRequest.Content.Headers.ContentType = new MediaTypeHeaderValue(JsonContentType);

        // Optional bearer token — absent for local LM Studio, present for authenticated endpoints.
        var apiKey = _configuration["Classifier:ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        using var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        using var response = await httpClient.SendAsync(httpRequest, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                $"Local chat endpoint returned status {response.StatusCode} ({(int)response.StatusCode}): {Truncate(body)}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(responseJson);

        // OpenAI shape: { "choices": [ { "message": { "content": "Sport" } } ] }
        if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
        {
            _logger.LogWarning("Local chat response had no choices; returning empty string. Endpoint={Endpoint}",
                opts.Endpoint);
            return string.Empty;
        }

        var content = choices[0]
            .GetProperty("message")
            .TryGetProperty("content", out var contentEl)
                ? contentEl.GetString() ?? string.Empty
                : string.Empty;

        return content;
    }

    private static string Truncate(string s, int max = 500) =>
        s.Length <= max ? s : s[..max] + "...";
}

/// <summary>
/// Minimal request body for OpenAI-compatible <c>/chat/completions</c>.
/// </summary>
internal sealed class ChatCompletionRequest
{
    public string Model { get; set; } = string.Empty;
    public double Temperature { get; set; }
    public int MaxTokens { get; set; }
    public ChatMessage[] Messages { get; set; } = [];
}

/// <summary>
/// A single chat message in an OpenAI-compatible request.
/// </summary>
internal sealed class ChatMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}
