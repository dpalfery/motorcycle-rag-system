using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Text;
using System.Text.RegularExpressions;

namespace MotorcycleRAG.Admin.Processing;

/// <summary>
/// Result of embedding generation
/// </summary>
public class EmbeddingResult
{
    public bool Success { get; set; }
    public float[] Embedding { get; set; } = Array.Empty<float>();
    public int Dimensions { get; set; }
    public string Error { get; set; } = string.Empty;
}

/// <summary>
/// Service for generating embeddings using ONNX Runtime
/// Supports sentence-transformers models like all-MiniLM-L6-v2
/// </summary>
public class OnnxEmbeddingService : IDisposable
{
    private readonly InferenceSession _session;
    private readonly string _modelPath;
    private readonly int _maxTokens;
    private bool _disposed;

    public OnnxEmbeddingService(string modelPath, int maxTokens = 256)
    {
        if (string.IsNullOrEmpty(modelPath))
            throw new ArgumentException("Model path cannot be null or empty", nameof(modelPath));
        
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"ONNX model not found at: {modelPath}", modelPath);

        _modelPath = modelPath;
        _maxTokens = maxTokens;

        try
        {
            var sessionOptions = new SessionOptions();
            sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            
            _session = new InferenceSession(modelPath, sessionOptions);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load ONNX model: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Generates an embedding for the given text
    /// </summary>
    public async Task<EmbeddingResult> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(OnnxEmbeddingService));

        var result = new EmbeddingResult();

        try
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                result.Error = "Input text cannot be empty";
                return result;
            }

            // Tokenize and prepare input
            var tokens = await Task.Run(() => TokenizeText(text), cancellationToken);
            
            if (tokens.Length == 0)
            {
                result.Error = "Tokenization produced no tokens";
                return result;
            }

            // Create input tensors
            var inputIds = new DenseTensor<long>(tokens, new[] { 1, tokens.Length });
            var attentionMask = new DenseTensor<long>(new long[tokens.Length].Select(x => 1L).ToArray(), 
                new[] { 1, tokens.Length });

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
                NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask)
            };

            // Run inference
            var outputs = await Task.Run(() => _session.Run(inputs), cancellationToken);
            
            // Extract embedding from output
            var outputTensor = outputs.FirstOrDefault()?.AsEnumerable<float>().ToArray();
            
            if (outputTensor == null || outputTensor.Length == 0)
            {
                result.Error = "Model produced no output";
                return result;
            }

            // Apply mean pooling if needed (for sentence-transformers models)
            var embedding = ApplyMeanPooling(outputTensor, tokens.Length);
            
            // Normalize embedding
            embedding = NormalizeEmbedding(embedding);

            result.Embedding = embedding;
            result.Dimensions = embedding.Length;
            result.Success = true;

            // Dispose outputs
            foreach (var output in outputs)
            {
                output?.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            result.Error = "Embedding generation was cancelled";
        }
        catch (Exception ex)
        {
            result.Error = $"Error generating embedding: {ex.Message}";
        }

        return result;
    }

    /// <summary>
    /// Generates embeddings for multiple texts in batch
    /// </summary>
    public async Task<List<EmbeddingResult>> GenerateEmbeddingsBatchAsync(
        IEnumerable<string> texts, 
        CancellationToken cancellationToken = default)
    {
        var results = new List<EmbeddingResult>();

        foreach (var text in texts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await GenerateEmbeddingAsync(text, cancellationToken);
            results.Add(result);
        }

        return results;
    }

    /// <summary>
    /// Basic tokenization (simplified - in production use proper tokenizer)
    /// </summary>
    private long[] TokenizeText(string text)
    {
        // Clean and normalize text
        text = text.ToLowerInvariant();
        text = Regex.Replace(text, @"[^\w\s]", " ");
        text = Regex.Replace(text, @"\s+", " ");
        text = text.Trim();

        // Split into words
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Truncate to max tokens
        if (words.Length > _maxTokens - 2) // Reserve space for special tokens
        {
            words = words.Take(_maxTokens - 2).ToArray();
        }

        // Simple vocabulary mapping (in production use actual vocabulary file)
        // Using hash-based mapping for demonstration
        var tokens = new List<long> { 101 }; // [CLS] token
        
        foreach (var word in words)
        {
            // Simple hash-based token ID (replace with actual vocabulary lookup)
            var tokenId = Math.Abs(word.GetHashCode()) % 30000 + 1000;
            tokens.Add(tokenId);
        }
        
        tokens.Add(102); // [SEP] token

        // Pad to fixed length if needed
        while (tokens.Count < _maxTokens)
        {
            tokens.Add(0); // [PAD] token
        }

        return tokens.Take(_maxTokens).ToArray();
    }

    /// <summary>
    /// Applies mean pooling to get sentence embedding
    /// </summary>
    private float[] ApplyMeanPooling(float[] tokenEmbeddings, int seqLength)
    {
        // Assume embeddings are in shape [seq_length, hidden_size]
        // Calculate the embedding dimension
        var hiddenSize = tokenEmbeddings.Length / seqLength;
        
        if (hiddenSize * seqLength != tokenEmbeddings.Length)
        {
            // If shape doesn't match, return as-is (might be pre-pooled)
            return tokenEmbeddings;
        }

        var pooled = new float[hiddenSize];

        for (int i = 0; i < hiddenSize; i++)
        {
            float sum = 0;
            for (int j = 0; j < seqLength; j++)
            {
                sum += tokenEmbeddings[j * hiddenSize + i];
            }
            pooled[i] = sum / seqLength;
        }

        return pooled;
    }

    /// <summary>
    /// Normalizes embedding to unit length (L2 normalization)
    /// </summary>
    private float[] NormalizeEmbedding(float[] embedding)
    {
        var norm = Math.Sqrt(embedding.Sum(x => x * x));
        
        if (norm < 1e-12) // Avoid division by zero
            return embedding;

        return embedding.Select(x => (float)(x / norm)).ToArray();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _session?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Factory for creating ONNX embedding services
/// </summary>
public static class OnnxEmbeddingServiceFactory
{
    /// <summary>
    /// Creates an embedding service with the default model
    /// </summary>
    public static OnnxEmbeddingService CreateFromAppResources()
    {
        // Look for embedded model in app resources
        var appPath = AppDomain.CurrentDomain.BaseDirectory;
        var modelPath = Path.Combine(appPath, "Resources", "Raw", "embedding-model.onnx");

        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException(
                "Embedding model not found. Please ensure embedding-model.onnx is included in Resources/Raw/",
                modelPath);
        }

        return new OnnxEmbeddingService(modelPath);
    }

    /// <summary>
    /// Creates an embedding service with a custom model path
    /// </summary>
    public static OnnxEmbeddingService CreateFromPath(string modelPath)
    {
        return new OnnxEmbeddingService(modelPath);
    }
}
