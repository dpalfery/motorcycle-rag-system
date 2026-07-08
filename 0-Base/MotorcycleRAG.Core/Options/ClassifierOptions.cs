using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Core.Options;

/// <summary>
/// Configuration for the D7 motorcycle category classifier (local LLM + cache behavior).
/// </summary>
/// <remarks>
/// &lt;para&gt;Binds to the <c>Classifier</c> configuration section. The chat endpoint defaults to the
/// local LM Studio OpenAI-compatible endpoint (<c>http://localhost:1234/v1</c>) that already backs
/// graph entity extraction in this repo (see Admin config <c>graphExtractionEndpoint</c>).&lt;/para&gt;
/// &lt;para&gt;&lt;b>R6 (prod reachability):</b&gt; the local endpoint will not exist inside Container Apps in
/// production. Override <see cref="Endpoint"/> per environment so prod can point at a real chat endpoint.&lt;/para&gt;
/// &lt;para&gt;&lt;b>R4 (accuracy/fallback):</b&gt; <see cref="FallbackCategory"/> is the defined category returned
/// when the LLM is unreachable or returns an unparsable response. It is logged and NOT written back
/// to the cache (so a transient outage cannot poison the authoritative cache with a guess).&lt;/para&gt;
/// </remarks>
public class ClassifierOptions
{
    /// <summary>
    /// OpenAI-compatible chat-completions base endpoint (no trailing slash required).
    /// Default: <c>http://localhost:1234/v1</c> (local LM Studio).
    /// </summary>
    [Url]
    public string Endpoint { get; set; } = "http://localhost:1234/v1";

    /// <summary>
    /// Model identifier passed in the <c>model</c> field of the chat-completions request.
    /// Default: <c>qwen3.5-9b</c> — the local Qwen3.5-9B chat model already used for graph extraction.
    /// </summary>
    /// <remarks>
    /// Keep in sync with the model name exposed by the local LM Studio server
    /// (Admin config <c>graphExtractionModel</c> / <c>tokenizerModelPath</c>).
    /// </remarks>
    [Required]
    public string Model { get; set; } = "qwen3.5-9b";

    /// <summary>
    /// Timeout in seconds for a single chat-completions request. Default: 30s.
    /// </summary>
    [Range(1, 600)]
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Category returned when the LLM is unreachable or returns an unparsable response.
    /// Must be one of: dirt, touring, sport, cruiser (case-insensitive). Default: <c>sport</c>.
    /// </summary>
    /// <remarks>
    /// This value is parsed into a <c>MotorcycleCategory</c> at use time; an invalid value is
    /// treated as <c>Sport</c> at runtime with a logged warning (never throws).
    /// </remarks>
    public string FallbackCategory { get; set; } = "sport";
}
