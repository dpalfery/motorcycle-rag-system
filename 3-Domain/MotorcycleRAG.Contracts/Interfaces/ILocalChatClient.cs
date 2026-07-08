namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Minimal OpenAI-compatible chat-completion client used by local-first features
/// (e.g. the D7 motorcycle category classifier backed by a local LM Studio / Qwen endpoint).
/// </summary>
/// <remarks>
/// &lt;para&gt;This abstraction keeps HTTP concerns out of the Application layer: implementations live in
/// Persistence and call <c>{Endpoint}/chat/completions</c> via <c>IHttpClientFactory</c>. The
/// interface signature is intentionally string-in / string-out so callers can apply their own
/// prompt engineering and output parsing without depending on any SDK type.&lt;/para&gt;
/// &lt;para&gt;Implementations should throw on transport/HTTP failure (standard <c>HttpClient</c> behavior);
/// callers are responsible for applying a defined fallback policy (e.g. via try/catch).&lt;/para&gt;
/// </remarks>
public interface ILocalChatClient
{
    /// <summary>
    /// Requests a chat completion for the supplied prompts.
    /// </summary>
    /// <param name="systemPrompt">The system/instruction prompt.</param>
    /// <param name="userPrompt">The user turn (the actual query / classification input).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The assistant's message content (first choice). May be an empty string if the endpoint
    /// returns no content; never <c>null</c> on a successful response.
    /// </returns>
    /// <exception cref="HttpRequestException">Thrown when the endpoint is unreachable or returns a non-success status.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the cancellation token fires.</exception>
    Task<string> GetChatCompletionAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default);
}
