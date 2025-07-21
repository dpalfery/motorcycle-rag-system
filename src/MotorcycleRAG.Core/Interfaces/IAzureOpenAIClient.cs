using MotorcycleRAG.Core.Models;

namespace MotorcycleRAG.Core.Interfaces;

/// <summary>
/// Interface for Azure OpenAI client with retry policies and circuit breaker
/// </summary>
public interface IAzureOpenAIClient
{
    /// <summary>
    /// Get chat completions with retry and circuit breaker
    /// </summary>
    Task<string> GetChatCompletionAsync(
        string deploymentName,
        string prompt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate embeddings with retry and circuit breaker
    /// </summary>
    Task<float[]> GetEmbeddingAsync(
        string deploymentName,
        string text,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate embeddings for multiple texts
    /// </summary>
    Task<float[][]> GetEmbeddingsAsync(
        string deploymentName,
        string[] texts,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if the client is healthy
    /// </summary>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}