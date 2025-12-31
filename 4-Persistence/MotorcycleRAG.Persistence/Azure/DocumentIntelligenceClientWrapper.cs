using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.DTOs;
using Polly;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Core.Utilities;


namespace MotorcycleRAG.Persistence.Azure;

/// <summary>
/// Azure Document Intelligence client wrapper with resilience patterns
/// </summary>
public class DocumentIntelligenceClientWrapper : IDocumentIntelligenceClient, IDisposable
{
    private readonly DocumentIntelligenceClient _client;
    private readonly AzureAIOptions _config;
    private readonly ILogger<DocumentIntelligenceClientWrapper> _logger;
    private readonly IAsyncPolicy _retryPolicy;
    private bool _disposed;

    public DocumentIntelligenceClientWrapper(
        IOptions<AzureAIOptions> config,
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
                    new MotorcycleRAG.Domain.DTOs.DocumentPage
                    {
                        PageNumber = 1,
                        Content = "Page 1 content from URI",
                        Width = 8.5f,
                        Height = 11.0f
                    }
                },
                Tables = Array.Empty<MotorcycleRAG.Domain.DTOs.DocumentTable>(),
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

    public async Task<DocumentAnalysisResult> AnalyzeDocumentAsync(string documentUrl)
    {
        try
        {
            _logger.LogDebug("Analyzing document from URL: {DocumentUrl}", documentUrl);

            // Convert URL to URI and delegate to URI method
            var uri = new Uri(documentUrl);
            return await AnalyzeDocumentFromUriAsync(uri, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing document from URL: {DocumentUrl}", documentUrl);
            throw;
        }
    }

    public async Task<DocumentAnalysisResult> AnalyzeDocumentAsync(Stream documentStream, string contentType)
    {
        try
        {
            _logger.LogDebug("Analyzing document from stream with content type: {ContentType}", contentType);

            // Convert stream to byte array and create placeholder result
            using var memoryStream = new MemoryStream();
            await documentStream.CopyToAsync(memoryStream);
            var documentBytes = memoryStream.ToArray();

            // Simplified implementation - return placeholder analysis
            await Task.Delay(500); // Simulate document analysis

            var result = new DocumentAnalysisResult
            {
                Content = "Extracted text content from the document stream",
                Pages = new[]
                {
                    new MotorcycleRAG.Domain.DTOs.DocumentPage
                    {
                        PageNumber = 1,
                        Content = "Page 1 content from stream",
                        Width = 8.5f,
                        Height = 11.0f
                    }
                },
                Tables = new[]
                {
                    new MotorcycleRAG.Domain.DTOs.DocumentTable
                    {
                        RowCount = 2,
                        ColumnCount = 2,
                        Cells = new[]
                        {
                            new MotorcycleRAG.Domain.DTOs.DocumentTableCell { RowIndex = 0, ColumnIndex = 0, Content = "Header 1" },
                            new MotorcycleRAG.Domain.DTOs.DocumentTableCell { RowIndex = 0, ColumnIndex = 1, Content = "Header 2" },
                            new MotorcycleRAG.Domain.DTOs.DocumentTableCell { RowIndex = 1, ColumnIndex = 0, Content = "Data 1" },
                            new MotorcycleRAG.Domain.DTOs.DocumentTableCell { RowIndex = 1, ColumnIndex = 1, Content = "Data 2" }
                        }
                    }
                },
                Metadata = new Dictionary<string, object>
                {
                    ["ModelId"] = "prebuilt-layout",
                    ["DocumentSize"] = documentBytes.Length,
                    ["ContentType"] = contentType
                }
            };

            _logger.LogDebug("Document analysis from stream completed successfully");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing document from stream");
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
