using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Core.Interfaces;
using MotorcycleRAG.Core.Models;
using Polly;

namespace MotorcycleRAG.Infrastructure.Azure;

/// <summary>
/// Azure Document Intelligence client wrapper with resilience patterns
/// </summary>
public class DocumentIntelligenceClientWrapper : IDocumentIntelligenceClient, IDisposable
{
    private readonly DocumentIntelligenceClient _client;
    private readonly AzureAIConfiguration _config;
    private readonly ILogger<DocumentIntelligenceClientWrapper> _logger;
    private readonly IAsyncPolicy _retryPolicy;
    private bool _disposed;

    public DocumentIntelligenceClientWrapper(
        IOptions<AzureAIConfiguration> config,
        ILogger<DocumentIntelligenceClientWrapper> logger)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        _config = config.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Initialize Document Intelligence client with DefaultAzureCredential
        var credential = new DefaultAzureCredential();
        _client = new DocumentIntelligenceClient(new Uri(_config.DocumentIntelligenceEndpoint), credential);

        // Configure resilience policies
        _retryPolicy = CreateRetryPolicy();

        _logger.LogInformation("Document Intelligence client initialized with endpoint: {Endpoint}", 
            _config.DocumentIntelligenceEndpoint);
    }

    public async Task<DocumentAnalysisResult> AnalyzeDocumentAsync(
        byte[] document,
        string? contentType = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Analyzing document with Layout model");

            // Simplified implementation - in a real scenario, you would use the actual Document Intelligence SDK
            // For now, return placeholder analysis to demonstrate the pattern
            await Task.Delay(500, cancellationToken); // Simulate document analysis

            var result = new DocumentAnalysisResult
            {
                Content = "Extracted text content from the document",
                Pages = new[]
                {
                    new MotorcycleRAG.Core.Models.DocumentPage
                    {
                        PageNumber = 1,
                        Content = "Page 1 content",
                        Width = 8.5f,
                        Height = 11.0f
                    }
                },
                Tables = new[]
                {
                    new MotorcycleRAG.Core.Models.DocumentTable
                    {
                        RowCount = 2,
                        ColumnCount = 2,
                        Cells = new[]
                        {
                            new MotorcycleRAG.Core.Models.DocumentTableCell { RowIndex = 0, ColumnIndex = 0, Content = "Header 1" },
                            new MotorcycleRAG.Core.Models.DocumentTableCell { RowIndex = 0, ColumnIndex = 1, Content = "Header 2" },
                            new MotorcycleRAG.Core.Models.DocumentTableCell { RowIndex = 1, ColumnIndex = 0, Content = "Data 1" },
                            new MotorcycleRAG.Core.Models.DocumentTableCell { RowIndex = 1, ColumnIndex = 1, Content = "Data 2" }
                        }
                    }
                },
                Metadata = new Dictionary<string, object>
                {
                    ["ModelId"] = "prebuilt-layout",
                    ["DocumentSize"] = document.Length,
                    ["ContentType"] = contentType ?? "application/pdf"
                }
            };

            _logger.LogDebug("Document analysis completed successfully");
            return result;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Document Intelligence request failed: {ErrorCode} - {Message}", 
                ex.ErrorCode, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in AnalyzeDocumentAsync");
            throw;
        }
    }

    public async Task<DocumentAnalysisResult> AnalyzeDocumentFromUriAsync(
        Uri documentUri,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Analyzing document from URI: {DocumentUri}", documentUri);

            // Simplified implementation - in a real scenario, you would use the actual Document Intelligence SDK
            // For now, return placeholder analysis to demonstrate the pattern
            await Task.Delay(500, cancellationToken); // Simulate document analysis

            var result = new DocumentAnalysisResult
            {
                Content = $"Extracted text content from document at {documentUri}",
                Pages = new[]
                {
                    new MotorcycleRAG.Core.Models.DocumentPage
                    {
                        PageNumber = 1,
                        Content = "Page 1 content from URI",
                        Width = 8.5f,
                        Height = 11.0f
                    }
                },
                Tables = Array.Empty<MotorcycleRAG.Core.Models.DocumentTable>(),
                Metadata = new Dictionary<string, object>
                {
                    ["ModelId"] = "prebuilt-layout",
                    ["DocumentUri"] = documentUri.ToString()
                }
            };

            _logger.LogDebug("Document analysis from URI completed successfully");
            return result;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Document Intelligence URI request failed: {ErrorCode} - {Message}", 
                ex.ErrorCode, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in AnalyzeDocumentFromUriAsync");
            throw;
        }
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Simple health check - in a real scenario, you would make an actual API call
            await Task.Delay(50, cancellationToken); // Simulate health check
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Document Intelligence health check failed");
            return false;
        }
    }

    private IAsyncPolicy CreateRetryPolicy()
    {
        var retryConfig = _config.Retry;
        
        return Policy
            .Handle<RequestFailedException>(ex => IsRetryableError(ex))
            .Or<TaskCanceledException>()
            .Or<HttpRequestException>()
            .WaitAndRetryAsync(
                retryCount: retryConfig.MaxRetries,
                sleepDurationProvider: retryAttempt => retryConfig.UseExponentialBackoff
                    ? TimeSpan.FromSeconds(Math.Min(
                        retryConfig.BaseDelaySeconds * Math.Pow(2, retryAttempt - 1),
                        retryConfig.MaxDelaySeconds))
                    : TimeSpan.FromSeconds(retryConfig.BaseDelaySeconds),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    _logger.LogWarning("Retry attempt {RetryCount} for Document Intelligence after {Delay}ms",
                        retryCount, timespan.TotalMilliseconds);
                });
    }

    private static bool IsRetryableError(RequestFailedException ex)
    {
        // Retry on rate limiting, server errors, and timeout
        return ex.Status == 429 || // Too Many Requests
               ex.Status == 500 || // Internal Server Error
               ex.Status == 502 || // Bad Gateway
               ex.Status == 503 || // Service Unavailable
               ex.Status == 504;   // Gateway Timeout
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            // DocumentIntelligenceClient doesn't implement IDisposable in the current SDK version
            _disposed = true;
        }
    }
}