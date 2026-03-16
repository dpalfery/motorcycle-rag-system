using System.Threading;


namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Interface for Azure Foundry client operations.
/// Chat completions are intentionally excluded — all LLM reasoning runs in Foundry Agent Service.
/// </summary>
public interface IAzureFoundryClient {
    /// <summary>
    /// Gets embeddings (single text convenience) from Azure Foundry
    /// </summary>
    Task<float[]> GetEmbeddingsAsync(string model, string text, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a single embedding from Azure Foundry
    /// </summary>
    Task<float[]> GetEmbeddingAsync(string model, string text, CancellationToken cancellationToken);

    /// <summary>
    /// Gets multiple embeddings from Azure Foundry
    /// </summary>
    Task<float[][]> GetEmbeddingsAsync(string model, string[] texts, CancellationToken cancellationToken);

    /// <summary>
    /// Processes multimodal (vision + text) content
    /// </summary>
    Task<string> ProcessMultimodalContentAsync(string model, string textPrompt, byte[] imageData, string imageContentType, CancellationToken cancellationToken);
}
