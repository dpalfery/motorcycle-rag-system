using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Application.Agents;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;
using MotorcycleRAG.Domain.ValueObjects;

namespace MotorcycleRAG.Application.Services;

/// <summary>
/// Orchestrates web scraping, content extraction, and indexing operations for web sources.
/// Coordinates between WebSearchAgent for content extraction and IMotorcycleIndexingService for indexing.
/// </summary>
public class WebScrapeOrchestrator : IWebScrapeOrchestrator {
    private readonly IWebScrapeRunRepository _webScrapeRunRepository;
    private readonly ISearchAgent _webSearchAgent;
    private readonly IMotorcycleIndexingService _indexingService;
    private readonly IWebSourceRepository _webSourceRepository;
    private readonly ILogger<WebScrapeOrchestrator> _logger;

    // Cancellation token management for active scrapes
    private readonly ConcurrentDictionary<long, CancellationTokenSource> _activeScrapes =
        new ConcurrentDictionary<long, CancellationTokenSource>();

    private static readonly string[] DefaultMotorcycleSearchTerms = {
        "motorcycle specifications",
        "motorcycle performance",
        "motorcycle maintenance",
        "motorcycle reviews",
        "motorcycle technical data"
    };

    public WebScrapeOrchestrator(
        IWebScrapeRunRepository webScrapeRunRepository,
        ISearchAgent webSearchAgent,
        IMotorcycleIndexingService indexingService,
        IWebSourceRepository webSourceRepository,
        ILogger<WebScrapeOrchestrator> logger) {
        _webScrapeRunRepository = webScrapeRunRepository ?? throw new ArgumentNullException(nameof(webScrapeRunRepository));
        _webSearchAgent = webSearchAgent ?? throw new ArgumentNullException(nameof(webSearchAgent));
        _indexingService = indexingService ?? throw new ArgumentNullException(nameof(indexingService));
        _webSourceRepository = webSourceRepository ?? throw new ArgumentNullException(nameof(webSourceRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Initiates a web scrape run for a specific web source and orchestrates the complete pipeline
    /// </summary>
    public async Task<long> StartScrapeRunAsync(int webSourceId, CancellationToken cancellationToken = default) {
        if (webSourceId <= 0) {
            throw new ArgumentException("Invalid web source ID", nameof(webSourceId));
        }

        try {
            _logger.LogInformation("Starting scrape run for web source {WebSourceId}", webSourceId);

            // Verify web source exists and is enabled
            var webSource = await _webSourceRepository.GetWebSourceByIdAsync(webSourceId);
            if (webSource == null) {
                throw new InvalidOperationException($"Web source with ID {webSourceId} not found");
            }

            if (!webSource.IsEnabled) {
                throw new InvalidOperationException($"Web source {webSourceId} is not enabled");
            }

            // Create a new scrape run record
            var runId = await _webScrapeRunRepository.CreateWebScrapeRunAsync(webSourceId);
            _logger.LogInformation("Created scrape run {RunId} for web source {WebSourceId}", runId, webSourceId);

            // Register cancellation token source for this run
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _activeScrapes.TryAdd(runId, cts);

            try {
                // Execute the scrape pipeline (fire and forget with error handling)
                _ = ExecuteScrapeAndIndexPipelineAsync(runId, webSource, cts.Token);
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Error initiating scrape pipeline for run {RunId}", runId);
                // Mark run as failed immediately
                await _webScrapeRunRepository.UpdateWebScrapeRunAsync(
                    runId,
                    status: ScrapeRunStatus.Failed,
                    pagesCrawled: 0,
                    pagesIndexed: 0,
                    errors: 1,
                    errorMessage: $"Failed to initiate scrape: {ex.Message}");
    
                throw new InvalidOperationException($"Error initiating scrape pipeline for run {runId}", ex);
            }

            return runId;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Failed to start scrape run for web source {WebSourceId}", webSourceId);
            throw new InvalidOperationException($"Failed to start scrape run for web source {webSourceId}", ex);
        }
    }

    /// <summary>
    /// Cancels a running scrape operation
    /// </summary>
    public async Task<bool> CancelScrapeRunAsync(long runId) {
        if (runId <= 0) {
            throw new ArgumentException("Invalid run ID", nameof(runId));
        }

        try {
            // Get the current scrape run to verify it's running
            var scrapeRun = await _webScrapeRunRepository.GetWebScrapeRunAsync(runId);
            if (scrapeRun == null) {
                throw new InvalidOperationException($"Scrape run {runId} not found");
            }

            if (!scrapeRun.IsRunning) {
                _logger.LogWarning("Scrape run {RunId} is not running (status: {Status})", runId, scrapeRun.Status);
                return false;
            }

            // Signal cancellation
            if (_activeScrapes.TryGetValue(runId, out var cts)) {
                await cts.CancelAsync();
                _logger.LogInformation("Cancellation signal sent for scrape run {RunId}", runId);
                return true;
            }

            _logger.LogWarning("No active cancellation token found for run {RunId}", runId);
            return false;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Failed to cancel scrape run {RunId}", runId);
            throw new InvalidOperationException($"Failed to cancel scrape run {runId}", ex);
        }
    }

    /// <summary>
    /// Gets the current status of a scrape run
    /// </summary>
    public async Task<WebScrapeRun?> GetScrapeRunStatusAsync(long runId) {
        if (runId <= 0) {
            throw new ArgumentException("Invalid run ID", nameof(runId));
        }

        try {
            var scrapeRun = await _webScrapeRunRepository.GetWebScrapeRunAsync(runId);
            return scrapeRun;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Failed to get scrape run status for run {RunId}", runId);
            throw new InvalidOperationException($"Failed to get scrape run status for run {runId}", ex);
        }
    }

    /// <summary>
    /// Gets recent scrape runs for a web source
    /// </summary>
    public async Task<WebScrapeRun[]> GetRecentScrapeRunsAsync(int webSourceId, int limit = 10) {
        if (webSourceId <= 0) {
            throw new ArgumentException("Invalid web source ID", nameof(webSourceId));
        }

        if (limit <= 0) {
            limit = 10;
        }

        try {
            var scrapeRuns = await _webScrapeRunRepository.GetRecentScrapeRunsAsync(webSourceId, limit);
            return scrapeRuns;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Failed to get recent scrape runs for web source {WebSourceId}", webSourceId);
            throw new InvalidOperationException($"Failed to get recent scrape runs for web source {webSourceId}", ex);
        }
    }

    /// <summary>
    /// Gets all active/running scrape operations
    /// </summary>
    public async Task<WebScrapeRun[]> GetActiveScrapeRunsAsync() {
        try {
            var activeScrapes = await _webScrapeRunRepository.GetActiveScrapeRunsAsync();
            return activeScrapes;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Failed to get active scrape runs");
            throw new InvalidOperationException("Failed to get active scrape runs", ex);
        }
    }

    #region Private Methods

    /// <summary>
    /// Executes the complete scrape and index pipeline for a web source
    /// </summary>
    private async Task ExecuteScrapeAndIndexPipelineAsync(
        long runId,
        WebSource webSource,
        CancellationToken cancellationToken) {
        var stopwatch = Stopwatch.StartNew();
        var pagesCrawled = 0;
        var pagesIndexed = 0;
        var errorsEncountered = 0;
        string? errorMessage = null;

        try {
            _logger.LogInformation("Executing scrape and index pipeline for run {RunId}, source URL: {SourceUrl}",
                runId, webSource.Url);

            // Update run status to Running
            var updateSuccessful = await _webScrapeRunRepository.UpdateWebScrapeRunAsync(
                runId,
                status: ScrapeRunStatus.Running,
                pagesCrawled: 0,
                pagesIndexed: 0,
                errors: 0);

            if (!updateSuccessful) {
                throw new InvalidOperationException($"Failed to update scrape run {runId} status");
            }

            // Step 1: Crawl content from the web source
            var crawlResult = await CrawlWebSourceAsync(webSource, cancellationToken);
            pagesCrawled = crawlResult.PagesCrawled;
            errorsEncountered = crawlResult.ErrorCount;

            if (pagesCrawled == 0) {
                throw new InvalidOperationException(
                    $"No pages were successfully crawled from {webSource.Url}. Errors: {string.Join("; ", crawlResult.Errors)}");
            }

            _logger.LogInformation("Crawled {PageCount} pages from web source {SourceUrl} for run {RunId}",
                pagesCrawled, webSource.Url, runId);

            // Step 2: Convert crawled content to motorcycle documents
            var documents = ConvertCrawledContentToDocuments(crawlResult, webSource);

            if (documents.Length == 0) {
                throw new InvalidOperationException(
                    "No documents could be extracted from crawled content");
            }

            _logger.LogDebug("Converted {DocumentCount} documents from crawled content for run {RunId}",
                documents.Length, runId);

            // Step 3: Index the documents
            var indexResult = await IndexDocumentsAsync(documents);
            pagesIndexed = indexResult.DocumentsIndexed;

            if (indexResult.Errors.Count > 0) {
                errorsEncountered += indexResult.Errors.Count;
                _logger.LogWarning("Encountered {ErrorCount} errors during indexing for run {RunId}",
                    indexResult.Errors.Count, runId);
            }

            stopwatch.Stop();

            // Mark run as completed successfully
            var finalUpdateSuccessful = await _webScrapeRunRepository.UpdateWebScrapeRunAsync(
                runId,
                status: ScrapeRunStatus.Completed,
                pagesCrawled: pagesCrawled,
                pagesIndexed: pagesIndexed,
                errors: errorsEncountered);

            if (!finalUpdateSuccessful) {
                _logger.LogError("Failed to mark scrape run {RunId} as completed", runId);
            }

            _logger.LogInformation(
                "Scrape and index pipeline completed for run {RunId}. Duration: {Duration}ms, Pages Crawled: {PagesCrawled}, Pages Indexed: {PagesIndexed}, Errors: {ErrorCount}",
                runId, stopwatch.ElapsedMilliseconds, pagesCrawled, pagesIndexed, errorsEncountered);

            // Update crawl date on the web source
            await UpdateWebSourceCrawlDateAsync(webSource.Id);
        }
        catch (OperationCanceledException ex) {
            stopwatch.Stop();
            _logger.LogWarning(ex, "Scrape run {RunId} was cancelled. Pages crawled: {PagesCrawled}, indexed: {PagesIndexed}",
                runId, pagesCrawled, pagesIndexed);

            errorMessage = "Scrape operation was cancelled by user";
            await _webScrapeRunRepository.UpdateWebScrapeRunAsync(
                runId,
                status: ScrapeRunStatus.Cancelled,
                pagesCrawled: pagesCrawled,
                pagesIndexed: pagesIndexed,
                errors: errorsEncountered,
                errorMessage: errorMessage);
        }
        catch (Exception ex) {
            stopwatch.Stop();
            _logger.LogError(ex,
                "Scrape and index pipeline failed for run {RunId}. Pages crawled: {PagesCrawled}, indexed: {PagesIndexed}",
                runId, pagesCrawled, pagesIndexed);

            errorMessage = TruncateErrorMessage(ex.Message, 1000);
            errorsEncountered++;

            // Mark run as failed
            var failureUpdateSuccessful = await _webScrapeRunRepository.UpdateWebScrapeRunAsync(
                runId,
                status: ScrapeRunStatus.Failed,
                pagesCrawled: pagesCrawled,
                pagesIndexed: pagesIndexed,
                errors: errorsEncountered,
                errorMessage: errorMessage);

            if (!failureUpdateSuccessful) {
                _logger.LogError("Failed to mark scrape run {RunId} as failed", runId);
            }
        }
        finally {
            // Clean up cancellation token source with safe disposal
            // CA2000: Using proper dispose pattern with try-finally for safe disposal
            CancellationTokenSource? cts = null;
            try
            {
                _activeScrapes.TryRemove(runId, out cts);
            }
            finally
            {
                cts?.Dispose();
            }
        }
    }

    /// <summary>
    /// Crawls content from a web source
    /// </summary>
    private async Task<CrawlResult> CrawlWebSourceAsync(WebSource webSource, CancellationToken cancellationToken) {
        var result = new CrawlResult();

        try {
            _logger.LogDebug("Starting web crawl for source: {SourceUrl}", webSource.Url);

            // Generate search terms to extract content from the web source
            var searchTerms = GenerateSearchTermsForSource(webSource);

            // Execute web search for each term to extract content
            var searchResults = new List<SearchResult>();
            var searchParams = new SearchParameters {
                MaxResults = 10,
                MinRelevanceScore = 0.5f,
                EnableCaching = false
            };

            foreach (var searchTerm in searchTerms) {
                cancellationToken.ThrowIfCancellationRequested();

                try {
                    _logger.LogDebug("Executing web search for term: {SearchTerm} from source {SourceUrl}",
                        searchTerm, webSource.Url);

                    var termResults = await _webSearchAgent.SearchAsync(searchTerm, searchParams);

                    // Filter results to only include content from the target web source
                    var sourceSpecificResults = termResults
                        .Where(r => IsResultFromWebSource(r, webSource))
                        .ToList();

                    searchResults.AddRange(sourceSpecificResults);
                    result.PagesCrawled += sourceSpecificResults.Count;

                    _logger.LogDebug("Retrieved {ResultCount} results for search term: {SearchTerm}",
                        sourceSpecificResults.Count, searchTerm);
                }
                catch (Exception ex) {
                    result.ErrorCount++;
                    result.Errors.Add($"Error searching for term '{searchTerm}': {ex.Message}");
                    _logger.LogWarning(ex, "Failed to crawl content for search term: {SearchTerm}", searchTerm);
                }
            }

            result.CrawledContent = searchResults;

            if (result.PagesCrawled == 0) {
                result.Errors.Add($"No content could be crawled from web source: {webSource.Url}");
            }

            _logger.LogInformation("Web crawl completed. Pages crawled: {PageCount}, Errors: {ErrorCount}",
                result.PagesCrawled, result.ErrorCount);

            return result;
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Web crawl failed for source: {SourceUrl}", webSource.Url);
            result.Errors.Add($"Web crawl failed: {ex.Message}");
            result.ErrorCount++;
            return result;
        }
    }

    /// <summary>
    /// Generates search terms appropriate for extracting content from a web source
    /// </summary>
    private List<string> GenerateSearchTermsForSource(WebSource webSource) {
        var searchTerms = new List<string>();

        // Note: Custom search terms not yet implemented - use default motorcycle terms
        // Add default motorcycle-related search terms
        if (searchTerms.Count == 0) {
            searchTerms.AddRange(DefaultMotorcycleSearchTerms);
        }

        _logger.LogDebug("Generated {TermCount} search terms for web source: {SourceUrl}",
            searchTerms.Count, webSource.Url);

        return searchTerms;
    }

    /// <summary>
    /// Determines if a search result is from the target web source
    /// </summary>
    private bool IsResultFromWebSource(SearchResult result, WebSource webSource) {
        if (result?.Source?.SourceUrl == null || webSource?.Url == null) {
            return false;
        }

        // Convert string URL to Uri for comparison
        string resultDomain = string.Empty;
        try {
            if (Uri.TryCreate(result.Source.SourceUrl, UriKind.Absolute, out var resultUri)) {
                var hostnameUri = ExtractHostnameFromUri(resultUri);
                resultDomain = hostnameUri.Host;
            }
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "Error extracting domain from result URL: {ResultUrl}", result.Source.SourceUrl);
        }

        // Extract domain from web source (convert string to Uri)
        string sourceDomain = string.Empty;
        try {
            if (Uri.TryCreate(webSource.Url, UriKind.Absolute, out var sourceUri)) {
                var hostnameUri = ExtractHostnameFromUri(sourceUri);
                sourceDomain = hostnameUri.Host;
            }
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "Error extracting domain from web source URL: {WebSourceUrl}", webSource.Url);
        }

        return !string.IsNullOrEmpty(resultDomain) &&
               resultDomain.Equals(sourceDomain, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Extracts the hostname from a URI
    /// </summary>
    private Uri ExtractHostnameFromUri(Uri url) {
        try {
            ArgumentNullException.ThrowIfNull(url);
            return new Uri($"{url.Scheme}://{url.Host}");
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "Error extracting hostname from URI: {Uri}", url);
            // Fallback: return the original URI's host with its scheme, or throw if not possible
            if (!string.IsNullOrEmpty(url?.Host) && !string.IsNullOrEmpty(url?.Scheme))
            {
                return new Uri($"{url.Scheme}://{url.Host}");
            }
            throw;
        }
    }

    /// <summary>
    /// Converts crawled search results to motorcycle documents
    /// </summary>
    private MotorcycleDocument[] ConvertCrawledContentToDocuments(
        CrawlResult crawlResult,
        WebSource webSource) {
        var documents = new List<MotorcycleDocument>();

        foreach (var searchResult in crawlResult.CrawledContent) {
            try {
                var document = new MotorcycleDocument {
                    Id = $"web_{searchResult.Id}_{DateTime.UtcNow.Ticks}",
                    Title = searchResult.Source.SourceName ?? webSource.Name,
                    Content = searchResult.Content,
                    Type = Domain.Enums.DocumentType.WebContent,
                    Metadata = new DocumentMetadata(),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                // Map SourceUrl safely
                if (Uri.TryCreate(webSource.Url, UriKind.Absolute, out var parsed)) {
                    document.Metadata.SourceUrl = parsed;
                }

                // Add additional metadata from search result and web source
                var additionalMetadata = new Dictionary<string, object> {
                    ["webSourceId"] = webSource.Id,
                    ["webSourceName"] = webSource.Name,
                    ["relevanceScore"] = searchResult.RelevanceScore,
                    ["quality"] = (int)(searchResult.RelevanceScore * 100),
                    ["indexedAt"] = DateTime.UtcNow
                };

                // Preserve metadata from search result
                if (searchResult.Metadata != null && searchResult.Metadata.Count > 0) {
                    foreach (var kvp in searchResult.Metadata) {
                        additionalMetadata[kvp.Key] = kvp.Value;
                    }
                }

                foreach (var kvp in additionalMetadata) {
                    document.Metadata.AdditionalProperties[kvp.Key] = kvp.Value;
                }

                documents.Add(document);
            }
            catch (Exception ex) {
                _logger.LogWarning(ex, "Failed to convert search result to document: {ResultId}",
                    searchResult.Id);
            }
        }

        _logger.LogDebug("Converted {DocumentCount} search results to motorcycle documents",
            documents.Count);

        return documents.ToArray();
    }

    /// <summary>
    /// Indexes motorcycle documents
    /// </summary>
    private async Task<BatchIndexingResult> IndexDocumentsAsync(
        MotorcycleDocument[] documents) {
        try {
            _logger.LogInformation("Starting indexing of {DocumentCount} documents", documents.Length);

            var indexResult = await _indexingService.IndexDocumentsAsync(documents);

            _logger.LogInformation("Indexing completed. Processed: {Processed}, Indexed: {Indexed}, Errors: {ErrorCount}",
                indexResult.DocumentsProcessed, indexResult.DocumentsIndexed, indexResult.Errors.Count);

            return indexResult;
        }
        catch (OperationCanceledException ex) {
            _logger.LogWarning(ex, "Indexing was cancelled");
            throw new OperationCanceledException("Indexing operation was cancelled", ex);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Indexing failed");
            throw new InvalidOperationException("Indexing failed", ex);
        }
    }

    /// <summary>
    /// Updates the crawl date on a web source
    /// </summary>
    private async Task UpdateWebSourceCrawlDateAsync(int webSourceId) {
        try {
            var webSource = await _webSourceRepository.GetWebSourceByIdAsync(webSourceId);
            if (webSource != null) {
                webSource.LastCrawledDate = DateTime.UtcNow;
                await _webSourceRepository.UpdateWebSourceAsync(webSource);
                _logger.LogDebug("Updated crawl date for web source {WebSourceId}", webSourceId);
            }
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "Failed to update crawl date for web source {WebSourceId}", webSourceId);
        }
    }

    /// <summary>
    /// Truncates error message to a maximum length
    /// </summary>
    private string TruncateErrorMessage(string message, int maxLength) {
        if (string.IsNullOrEmpty(message)) {
            return string.Empty;
        }

        if (message.Length <= maxLength) {
            return message;
        }

        return string.Concat(message.AsSpan(0, maxLength - 3), "...");
    }

    #endregion

    #region Helper Classes

    /// <summary>
    /// Result of crawling a web source
    /// </summary>
    private class CrawlResult {
        public int PagesCrawled { get; set; }
        public int ErrorCount { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<SearchResult> CrawledContent { get; set; } = new();
    }

    #endregion
}
