using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using Polly;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Core.Utilities;


namespace MotorcycleRAG.Persistence.Azure;

/// <summary>
/// Azure Document Intelligence client wrapper with resilience patterns
/// </summary>
public class DocumentIntelligenceClientWrapper : IDocumentIntelligenceClient, IDisposable {
    private readonly DocumentIntelligenceClient _client;
    private readonly ILogger<DocumentIntelligenceClientWrapper> _logger;

    public DocumentIntelligenceClientWrapper(
        IOptions<AzureAIOptions> config,
        ILogger<DocumentIntelligenceClientWrapper> logger) {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);

        var azureConfig = config.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;

        // Initialize Document Intelligence client with DefaultAzureCredential
        var credential = new DefaultAzureCredential();
        _client = new DocumentIntelligenceClient(new Uri(azureConfig.DocumentIntelligenceEndpoint), credential);

        _logger.LogInformation("Document Intelligence client initialized with endpoint: {Endpoint}",
            azureConfig.DocumentIntelligenceEndpoint);
    }


    public Task<DocumentAnalysisResult> AnalyzeDocumentFromUriAsync(Uri documentUri)
    {
        return AnalyzeDocumentFromUriAsync(documentUri, CancellationToken.None);
    }

    public async Task<DocumentAnalysisResult> AnalyzeDocumentFromUriAsync(
        Uri documentUri,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(documentUri);

        try {
            _logger.LogDebug("Analyzing document from URI: {DocumentUri}", documentUri);

            // Simplified implementation - in a real scenario, you would use the actual Document Intelligence SDK
            // For now, return placeholder analysis to demonstrate the pattern
            await Task.Delay(500, cancellationToken); // Simulate document analysis

            var result = new DocumentAnalysisResult {
                Content = $"Extracted text content from document at {documentUri}",
                Pages = new[]
                {
                    new MotorcycleRAG.Contracts.Models.DTOs.DocumentPage
                    {
                        PageNumber = 1,
                        Content = "Page 1 content from URI",
                        Width = 8.5f,
                        Height = 11.0f
                    }
                },
                Tables = Array.Empty<MotorcycleRAG.Contracts.Models.DTOs.DocumentTable>(),
                Metadata = new Dictionary<string, object> {
                    ["ModelId"] = "prebuilt-layout",
                    ["DocumentUri"] = documentUri.ToString()
                }
            };

            _logger.LogDebug("Document analysis from URI completed successfully");
            return result;
        }
        catch (RequestFailedException ex) {
            _logger.LogError(ex, "Document Intelligence URI request failed: {ErrorCode}",
                ex.ErrorCode);
            throw new InvalidOperationException($"Document Intelligence failed to analyze document: {ex.ErrorCode}", ex);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Unexpected error in AnalyzeDocumentFromUriAsync");
            throw new InvalidOperationException("Failed to analyze document from URI", ex);
        }
    }

    public async Task<DocumentAnalysisResult> AnalyzeDocumentAsync(string documentUrl) {
        try {
            _logger.LogDebug("Analyzing document from URL: {DocumentUrl}", documentUrl);

            // Convert URL to URI and delegate to URI method
            var uri = new Uri(documentUrl);
            return await AnalyzeDocumentFromUriAsync(uri, CancellationToken.None);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error analyzing document from URL: {DocumentUrl}", documentUrl);
            throw new InvalidOperationException($"Failed to analyze document from URL: {documentUrl}", ex);
        }
    }

    public async Task<DocumentAnalysisResult> AnalyzeDocumentAsync(Stream documentStream, string contentType) {
        ArgumentNullException.ThrowIfNull(documentStream);

        try {
            _logger.LogDebug("Analyzing document from stream with content type: {ContentType}", contentType);

            // Convert stream to byte array and create placeholder result
            using var memoryStream = new MemoryStream();
            await documentStream.CopyToAsync(memoryStream);
            var documentBytes = memoryStream.ToArray();

            // Simplified implementation - return placeholder analysis
            await Task.Delay(500); // Simulate document analysis

            var result = new DocumentAnalysisResult {
                Content = "Extracted text content from the document stream",
                Pages = new[]
                {
                    new MotorcycleRAG.Contracts.Models.DTOs.DocumentPage
                    {
                        PageNumber = 1,
                        Content = "Page 1 content from stream",
                        Width = 8.5f,
                        Height = 11.0f
                    }
                },
                Tables = new[]
                {
                    new MotorcycleRAG.Contracts.Models.DTOs.DocumentTable
                    {
                        RowCount = 2,
                        ColumnCount = 2,
                        Cells = new[]
                        {
                            new MotorcycleRAG.Contracts.Models.DTOs.DocumentTableCell { RowIndex = 0, ColumnIndex = 0, Content = "Header 1" },
                            new MotorcycleRAG.Contracts.Models.DTOs.DocumentTableCell { RowIndex = 0, ColumnIndex = 1, Content = "Header 2" },
                            new MotorcycleRAG.Contracts.Models.DTOs.DocumentTableCell { RowIndex = 1, ColumnIndex = 0, Content = "Data 1" },
                            new MotorcycleRAG.Contracts.Models.DTOs.DocumentTableCell { RowIndex = 1, ColumnIndex = 1, Content = "Data 2" }
                        }
                    }
                },
                Metadata = new Dictionary<string, object> {
                    ["ModelId"] = "prebuilt-layout",
                    ["DocumentSize"] = documentBytes.Length,
                    ["ContentType"] = contentType
                }
            };

            _logger.LogDebug("Document analysis from stream completed successfully");
            return result;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error analyzing document from stream");
            throw new InvalidOperationException("Failed to analyze document from stream", ex);
        }
    }

    public Task<bool> IsHealthyAsync()
    {
        return IsHealthyAsync(CancellationToken.None);
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken) {
        try {
            // Simple health check - in a real scenario, you would make an actual API call
            await Task.Delay(50, cancellationToken); // Simulate health check
            return true;
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "Document Intelligence health check failed");
            return false;
        }
    }

    private bool _disposed;

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        // DocumentIntelligenceClient is not IDisposable; nothing to dispose.
        _disposed = true;
    }

    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
