using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Utilities;

namespace MotorcycleRAG.Application.Services;

/// <summary>
/// Service for managing web source registry operations
/// </summary>
public class WebSourceRegistryService {
    private readonly IWebSourceRepository _webSourceRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<WebSourceRegistryService> _logger;

    public WebSourceRegistryService(
        IWebSourceRepository webSourceRepository,
        ICurrentUserService currentUserService,
        ILogger<WebSourceRegistryService> logger) {
        _webSourceRepository = webSourceRepository ?? throw new ArgumentNullException(nameof(webSourceRepository));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Adds a new web source to the registry
    /// </summary>
    public async Task<WebSource> AddWebSourceAsync(WebSource webSource) {
        ArgumentNullException.ThrowIfNull(webSource);

        // Verify authorization
        if (!_currentUserService.IsAuthenticated) {
            throw new UnauthorizedAccessException("User must be authenticated to add web sources");
        }

        if (!_currentUserService.IsInRole("mcr-api-admin")) {
            throw new UnauthorizedAccessException("User does not have permission to add web sources");
        }

        // Check if URL already exists
        if (string.IsNullOrWhiteSpace(webSource.Url)) {
            throw new InvalidOperationException("Web source URL must not be null or empty");
        }

        Uri urlUri = new Uri(webSource.Url);
        var existingSource = await _webSourceRepository.GetWebSourceByUrlAsync(urlUri);
        if (existingSource != null) {
            throw new InvalidOperationException($"A web source with URL '{webSource.Url}' already exists");
        }

        var createdSource = await _webSourceRepository.CreateWebSourceAsync(webSource);
        _logger.LogInformation("Added web source {SourceName} with ID {SourceId} by user {UserId}",
            LogSanitizer.Sanitize(createdSource.Name), LogSanitizer.Sanitize(createdSource.Id), LogSanitizer.Sanitize(_currentUserService.UserId));  // codeql[cs/log-forging]

        return createdSource;
    }

    /// <summary>
    /// Updates an existing web source
    /// </summary>
    public async Task<WebSource> UpdateWebSourceAsync(WebSource webSource) {
        ArgumentNullException.ThrowIfNull(webSource);

        if (webSource.Id <= 0) {
            throw new ArgumentException("Invalid web source ID", nameof(webSource));
        }

        // Verify authorization
        if (!_currentUserService.IsAuthenticated) {
            throw new UnauthorizedAccessException("User must be authenticated to update web sources");
        }

        if (!_currentUserService.IsInRole("mcr-api-admin")) {
            throw new UnauthorizedAccessException("User does not have permission to update web sources");
        }

        // Verify source exists
        var existingSource = await _webSourceRepository.GetWebSourceByIdAsync(webSource.Id);
        if (existingSource == null) {
            throw new InvalidOperationException($"Web source with ID {webSource.Id} not found");
        }

        // Update timestamp
        webSource.LastUpdatedDate = DateTime.UtcNow;
        var updated = await _webSourceRepository.UpdateWebSourceAsync(webSource);

        if (!updated) {
            throw new InvalidOperationException($"Failed to update web source {webSource.Id}");
        }

        _logger.LogInformation("Updated web source {SourceId} by user {UserId}",
            webSource.Id, _currentUserService.UserId);
        return webSource;
    }

    /// <summary>
    /// Removes a web source from the registry
    /// </summary>
    public async Task RemoveWebSourceAsync(int webSourceId) {
        if (webSourceId <= 0) {
            throw new ArgumentException("Invalid web source ID", nameof(webSourceId));
        }

        // Verify authorization
        if (!_currentUserService.IsAuthenticated) {
            throw new UnauthorizedAccessException("User must be authenticated to remove web sources");
        }

        if (!_currentUserService.IsInRole("mcr-api-admin")) {
            throw new UnauthorizedAccessException("User does not have permission to remove web sources");
        }

        // Verify source exists
        var existingSource = await _webSourceRepository.GetWebSourceByIdAsync(webSourceId);
        if (existingSource == null) {
            throw new InvalidOperationException($"Web source with ID {webSourceId} not found");
        }

        var deleted = await _webSourceRepository.DeleteWebSourceAsync(webSourceId);

        if (!deleted) {
            throw new InvalidOperationException($"Failed to delete web source {webSourceId}");
        }

        _logger.LogInformation("Removed web source {SourceId} by user {UserId}",
            webSourceId, _currentUserService.UserId);
    }

    /// <summary>
    /// Gets all web sources
    /// </summary>
    public async Task<WebSource[]> GetAllSourcesAsync() {
        return await _webSourceRepository.GetAllWebSourcesAsync();
    }

    /// <summary>
    /// Gets enabled web sources for search
    /// </summary>
    public async Task<WebSource[]> GetEnabledSourcesAsync() {
        var allSources = await _webSourceRepository.GetAllWebSourcesAsync();
        return allSources.Where(s => s.IsEnabled && s.IncludeInSearch).ToArray();
    }

    /// <summary>
    /// Gets a web source by ID
    /// </summary>
    public async Task<WebSource?> GetSourceByIdAsync(int webSourceId) {
        return await _webSourceRepository.GetWebSourceByIdAsync(webSourceId);
    }

    /// <summary>
    /// Updates the last crawled date for a web source
    /// </summary>
    public async Task UpdateCrawlDateAsync(int webSourceId) {
        var source = await _webSourceRepository.GetWebSourceByIdAsync(webSourceId);
        if (source == null) {
            throw new InvalidOperationException($"Web source with ID {webSourceId} not found");
        }

        source.LastCrawledDate = DateTime.UtcNow;
        await _webSourceRepository.UpdateWebSourceAsync(source);
        _logger.LogDebug("Updated crawl date for web source {SourceId}", webSourceId);
    }
}
