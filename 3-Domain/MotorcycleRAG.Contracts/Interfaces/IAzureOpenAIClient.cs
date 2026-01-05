using MotorcycleRAG.Contracts.Models.DTOs;
using System.Threading;


namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Interface for Azure OpenAI client operations (cancellation required)
/// </summary>
public interface IAzureOpenAIClient {
    /// <summary>
    /// Gets chat completion from Azure OpenAI
    /// </summary>
    Task<string> GetChatCompletionAsync(string deploymentName, string prompt, CancellationToken cancellationToken);

    /// <summary>
    /// Gets embeddings (single text convenience) from Azure OpenAI
    /// </summary>
    Task<float[]> GetEmbeddingsAsync(string model, string text, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a single embedding from Azure OpenAI
    /// </summary>
    Task<float[]> GetEmbeddingAsync(string model, string text, CancellationToken cancellationToken);

    /// <summary>
    /// Gets multiple embeddings from Azure OpenAI
    /// </summary>
    Task<float[][]> GetEmbeddingsAsync(string model, string[] texts, CancellationToken cancellationToken);

    /// <summary>
    /// Processes multimodal (vision + text) content
    /// </summary>
    Task<string> ProcessMultimodalContentAsync(string model, string textPrompt, byte[] imageData, string imageContentType, CancellationToken cancellationToken);
}
