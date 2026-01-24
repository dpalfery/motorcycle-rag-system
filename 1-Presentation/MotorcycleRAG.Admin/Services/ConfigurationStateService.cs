using Microsoft.Extensions.Logging;
using MotorcycleRAG.Admin.Constants;

namespace MotorcycleRAG.Admin.Services;

/// <summary>
/// Implementation of IConfigurationStateService that loads and persists configuration
/// via ISettingsService. Provides validation for API URLs and auth settings.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance", "CA1812: Avoid uninstantiated internal classes",
    Justification = "Instantiated by dependency injection")]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design", "S3059:Types should not have members with visibility set higher than the type's visibility",
    Justification = "Internal class implements public interface IConfigurationStateService required by clients in other assemblies")]
internal sealed class ConfigurationStateService : IConfigurationStateService
{
    private readonly ISettingsService _settingsService;
    private readonly ILogger<ConfigurationStateService> _logger;

    private Uri? _apiBaseUrl;
    private string? _authClientId;
    private string? _authAuthority;
    private string? _authScope;
    private string? _embeddingModelPath;

    /// <inheritdoc/>
    public bool IsConfigured => IsApiConfigured && IsAuthConfigured;

    /// <inheritdoc/>
    public bool IsApiConfigured => _apiBaseUrl != null && IsValidApiUrl(_apiBaseUrl);

    /// <inheritdoc/>
    public bool IsAuthConfigured =>
        !string.IsNullOrWhiteSpace(_authClientId) &&
        !string.IsNullOrWhiteSpace(_authAuthority) &&
        !string.IsNullOrWhiteSpace(_authScope);

    /// <inheritdoc/>
    public Uri? ApiBaseUrl => _apiBaseUrl;

    /// <inheritdoc/>
    public string? AuthClientId => _authClientId;

    /// <inheritdoc/>
    public string? AuthAuthority => _authAuthority;

    /// <inheritdoc/>
    public string? AuthScope => _authScope;

    /// <inheritdoc/>
    public string? EmbeddingModelPath => _embeddingModelPath;

    /// <inheritdoc/>
    public event EventHandler? ConfigurationChanged;

    public ConfigurationStateService(
        ISettingsService settingsService,
        ILogger<ConfigurationStateService> logger)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task LoadConfigurationAsync()
    {
        _logger.LogDebug("Loading configuration from settings");

        try
        {
            // Load regular settings
            var apiBaseUrlString = await _settingsService.GetAsync(SettingsKeys.ApiBaseUrl).ConfigureAwait(false);
            _authAuthority = await _settingsService.GetAsync(SettingsKeys.AuthAuthority).ConfigureAwait(false);
            _authScope = await _settingsService.GetAsync(SettingsKeys.AuthScope).ConfigureAwait(false);
            _embeddingModelPath = await _settingsService.GetAsync(SettingsKeys.EmbeddingModelPath).ConfigureAwait(false);

            // Load secure setting (client ID)
            _authClientId = await _settingsService.GetSecureAsync(SettingsKeys.AuthClientId).ConfigureAwait(false);

            // Parse API URL
            _apiBaseUrl = !string.IsNullOrWhiteSpace(apiBaseUrlString) && Uri.TryCreate(apiBaseUrlString, UriKind.Absolute, out var parsedUri)
                ? parsedUri
                : null;

            // Convert empty strings to null for cleaner checks
            _authAuthority = NullIfEmpty(_authAuthority);
            _authScope = NullIfEmpty(_authScope);
            _authClientId = NullIfEmpty(_authClientId);
            _embeddingModelPath = NullIfEmpty(_embeddingModelPath);

            _logger.LogInformation(
                "Configuration loaded. IsApiConfigured: {IsApi}, IsAuthConfigured: {IsAuth}",
                IsApiConfigured,
                IsAuthConfigured);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load configuration from settings");
            // Don't throw - app should still start with no configuration
        }
    }

    /// <inheritdoc/>
    public async Task SaveApiBaseUrlAsync(Uri? url)
    {
        if (url != null && !IsValidApiUrl(url))
        {
            throw new ArgumentException("Invalid API URL format or protocol", nameof(url));
        }

        var urlString = url?.ToString() ?? string.Empty;
        await _settingsService.SetAsync(SettingsKeys.ApiBaseUrl, urlString).ConfigureAwait(false);
        _apiBaseUrl = url;

        _logger.LogInformation("API base URL saved. IsApiConfigured: {IsConfigured}", IsApiConfigured);
        OnConfigurationChanged();
    }

    /// <inheritdoc/>
    public async Task SaveAuthConfigurationAsync(string clientId, string authority, string scope)
    {
        ArgumentNullException.ThrowIfNull(clientId);
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(scope);

        // Validate authority URL format
        if (!string.IsNullOrWhiteSpace(authority) && !Uri.TryCreate(authority, UriKind.Absolute, out _))
        {
            throw new ArgumentException("Invalid authority URL format", nameof(authority));
        }

        // Client ID is stored securely
        await _settingsService.SetSecureAsync(SettingsKeys.AuthClientId, clientId).ConfigureAwait(false);
        await _settingsService.SetAsync(SettingsKeys.AuthAuthority, authority).ConfigureAwait(false);
        await _settingsService.SetAsync(SettingsKeys.AuthScope, scope).ConfigureAwait(false);

        _authClientId = NullIfEmpty(clientId);
        _authAuthority = NullIfEmpty(authority);
        _authScope = NullIfEmpty(scope);

        _logger.LogInformation("Auth configuration saved. IsAuthConfigured: {IsConfigured}", IsAuthConfigured);
        OnConfigurationChanged();
    }

    /// <inheritdoc/>
    public async Task SaveEmbeddingModelPathAsync(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        await _settingsService.SetAsync(SettingsKeys.EmbeddingModelPath, path).ConfigureAwait(false);
        _embeddingModelPath = NullIfEmpty(path);

        _logger.LogInformation("Embedding model path saved");
        OnConfigurationChanged();
    }

    /// <inheritdoc/>
    public async Task ClearConfigurationAsync()
    {
        await _settingsService.RemoveAsync(SettingsKeys.ApiBaseUrl).ConfigureAwait(false);
        await _settingsService.RemoveSecureAsync(SettingsKeys.AuthClientId).ConfigureAwait(false);
        await _settingsService.RemoveAsync(SettingsKeys.AuthAuthority).ConfigureAwait(false);
        await _settingsService.RemoveAsync(SettingsKeys.AuthScope).ConfigureAwait(false);
        await _settingsService.RemoveAsync(SettingsKeys.EmbeddingModelPath).ConfigureAwait(false);

        _apiBaseUrl = null;
        _authClientId = null;
        _authAuthority = null;
        _authScope = null;
        _embeddingModelPath = null;

        _logger.LogInformation("Configuration cleared");
        OnConfigurationChanged();
    }

    /// <summary>
    /// Validates that the API URL is well-formed and uses appropriate protocol.
    /// Non-localhost URLs must use HTTPS.
    /// </summary>
    private static bool IsValidApiUrl(Uri uri)
    {
        // Allow HTTP only for localhost development
        if (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
        {
            return uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                   uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase);
        }

        return uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns null if the string is null or whitespace, otherwise returns the string.
    /// </summary>
    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// Raises the ConfigurationChanged event.
    /// </summary>
    private void OnConfigurationChanged()
    {
        ConfigurationChanged?.Invoke(this, EventArgs.Empty);
    }
}
