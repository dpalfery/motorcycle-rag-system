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
internal sealed class ConfigurationStateService : IConfigurationStateService {
    private readonly ISettingsService _settingsService;
    private readonly ILogger<ConfigurationStateService> _logger;

    private Uri? _apiBaseUrl;
    private string? _authClientId;
    private string? _authAuthority;
    private string? _authScope;
    private string? _embeddingProviderEndpoint;
    private string? _embeddingModel;
    private Uri? _localProcessorEndpoint;
    private string? _localProcessorWorkingDirectory;
    private string? _localProcessorStartCommand;
    private string? _pdfChunkerTokenizer;
    private int _pdfChunkerMaxTokens;
    private int _csvChunkMaxTokens;
    private string? _localProcessorUploadJobSecret;
    private bool _isLocalProcessorConfigured;

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
    public string? EmbeddingProviderEndpoint => _embeddingProviderEndpoint;

    /// <inheritdoc/>
    public string? EmbeddingModel => _embeddingModel;

    /// <inheritdoc/>
    public Uri? LocalProcessorEndpoint => _localProcessorEndpoint;

    /// <inheritdoc/>
    public string? LocalProcessorWorkingDirectory => _localProcessorWorkingDirectory;

    /// <inheritdoc/>
    public string? LocalProcessorStartCommand => _localProcessorStartCommand;

    /// <inheritdoc/>
    public int PdfChunkerMaxTokens => _pdfChunkerMaxTokens;

    /// <inheritdoc/>
    public int CsvChunkMaxTokens => _csvChunkMaxTokens;

    /// <inheritdoc/>
    public string? PdfChunkerTokenizer => _pdfChunkerTokenizer;

    /// <inheritdoc/>
    public string? LocalProcessorUploadJobSecret => _localProcessorUploadJobSecret;

    /// <inheritdoc/>
    public bool IsLocalProcessorConfigured => _isLocalProcessorConfigured;

    /// <inheritdoc/>
    public event EventHandler? ConfigurationChanged;

    public ConfigurationStateService(
        ISettingsService settingsService,
        ILogger<ConfigurationStateService> logger) {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task LoadConfigurationAsync() {
        _logger.LogDebug("Loading configuration from settings");

        try {
            // Load regular settings
            var apiBaseUrlString = await _settingsService.GetAsync(SettingsKeys.ApiBaseUrl).ConfigureAwait(false);
            _authAuthority = await _settingsService.GetAsync(SettingsKeys.AuthAuthority).ConfigureAwait(false);
            _authScope = await _settingsService.GetAsync(SettingsKeys.AuthScope).ConfigureAwait(false);
            _embeddingProviderEndpoint = await _settingsService.GetAsync(SettingsKeys.EmbeddingProviderEndpoint).ConfigureAwait(false);
            _embeddingModel = await _settingsService.GetAsync(SettingsKeys.EmbeddingModel).ConfigureAwait(false);
            var localProcessorEndpointString = await _settingsService.GetAsync(SettingsKeys.LocalProcessorEndpoint).ConfigureAwait(false);
            _localProcessorWorkingDirectory = await _settingsService.GetAsync(SettingsKeys.LocalProcessorWorkingDirectory).ConfigureAwait(false);
            _localProcessorStartCommand = await _settingsService.GetAsync(SettingsKeys.LocalProcessorStartCommand).ConfigureAwait(false);
            _pdfChunkerTokenizer = await _settingsService.GetAsync(SettingsKeys.PdfChunkerTokenizer).ConfigureAwait(false);
            _pdfChunkerMaxTokens = int.TryParse(await _settingsService.GetAsync(SettingsKeys.PdfChunkerMaxTokens).ConfigureAwait(false), out var pdfTokens)
                ? pdfTokens
                : LocalProcessorDefaults.DefaultPdfChunkerMaxTokens;
            _csvChunkMaxTokens = int.TryParse(await _settingsService.GetAsync(SettingsKeys.CsvChunkMaxTokens).ConfigureAwait(false), out var csvTokens)
                ? csvTokens
                : LocalProcessorDefaults.DefaultCsvChunkMaxTokens;

            // Load secure settings
            _authClientId = await _settingsService.GetSecureAsync(SettingsKeys.AuthClientId).ConfigureAwait(false);
            _localProcessorUploadJobSecret = await _settingsService.GetSecureAsync(SettingsKeys.LocalProcessorUploadJobSecret).ConfigureAwait(false);

            // Parse API URL
            _apiBaseUrl = !string.IsNullOrWhiteSpace(apiBaseUrlString) && Uri.TryCreate(apiBaseUrlString, UriKind.Absolute, out var parsedUri)
                ? parsedUri
                : null;
            _localProcessorEndpoint = !string.IsNullOrWhiteSpace(localProcessorEndpointString)
                && Uri.TryCreate(localProcessorEndpointString, UriKind.Absolute, out var parsedLocalProcessorUri)
                    ? parsedLocalProcessorUri
                    : new Uri(LocalProcessorDefaults.DefaultEndpoint);

            // Convert empty strings to null for cleaner checks
            _authAuthority = NullIfEmpty(_authAuthority);
            _authScope = NullIfEmpty(_authScope);
            _authClientId = NullIfEmpty(_authClientId);
            _embeddingProviderEndpoint = NullIfEmpty(_embeddingProviderEndpoint);
            _embeddingModel = NullIfEmpty(_embeddingModel);
            _localProcessorWorkingDirectory = NullIfEmpty(_localProcessorWorkingDirectory) ?? LocalProcessorDefaults.TryFindWorkingDirectory();
            _localProcessorStartCommand = NullIfEmpty(_localProcessorStartCommand) ?? LocalProcessorDefaults.DefaultStartCommand;
            _pdfChunkerTokenizer = NullIfEmpty(_pdfChunkerTokenizer) ?? LocalProcessorDefaults.DefaultPdfChunkerTokenizer;
            _pdfChunkerMaxTokens = _pdfChunkerMaxTokens == 0 ? LocalProcessorDefaults.DefaultPdfChunkerMaxTokens : _pdfChunkerMaxTokens;
            _csvChunkMaxTokens = _csvChunkMaxTokens == 0 ? LocalProcessorDefaults.DefaultCsvChunkMaxTokens : _csvChunkMaxTokens;
            _localProcessorUploadJobSecret = NullIfEmpty(_localProcessorUploadJobSecret);
            RefreshLocalProcessorConfigurationState();

            _logger.LogInformation(
                "Configuration loaded. IsApiConfigured: {IsApi}, IsAuthConfigured: {IsAuth}, IsLocalProcessorConfigured: {IsLocalProcessor}",
                IsApiConfigured,
                IsAuthConfigured,
                IsLocalProcessorConfigured);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Failed to load configuration from settings");
            // Don't throw - app should still start with no configuration
        }
    }

    /// <inheritdoc/>
    public async Task SaveApiBaseUrlAsync(Uri? url) {
        if (url != null && !IsValidApiUrl(url)) {
            throw new ArgumentException("Invalid API URL format or protocol", nameof(url));
        }

        var urlString = url?.ToString() ?? string.Empty;
        await _settingsService.SetAsync(SettingsKeys.ApiBaseUrl, urlString).ConfigureAwait(false);
        _apiBaseUrl = url;

        _logger.LogInformation("API base URL saved. IsApiConfigured: {IsConfigured}", IsApiConfigured);
        OnConfigurationChanged();
    }

    /// <inheritdoc/>
    public async Task SaveAuthConfigurationAsync(string clientId, string authority, string scope) {
        ArgumentNullException.ThrowIfNull(clientId);
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(scope);

        // Validate authority URL format
        if (!string.IsNullOrWhiteSpace(authority) && !Uri.TryCreate(authority, UriKind.Absolute, out _)) {
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
    public async Task SaveEmbeddingConfigurationAsync(string? providerEndpoint, string? model) {
        if (!string.IsNullOrWhiteSpace(providerEndpoint) && !Uri.TryCreate(providerEndpoint, UriKind.Absolute, out _)) {
            throw new ArgumentException("Invalid embedding provider endpoint format", nameof(providerEndpoint));
        }

        var normalizedProviderEndpoint = string.IsNullOrWhiteSpace(providerEndpoint)
            ? string.Empty
            : new Uri(providerEndpoint).AbsoluteUri.TrimEnd('/');
        var normalizedModel = model?.Trim() ?? string.Empty;

        await _settingsService.SetAsync(SettingsKeys.EmbeddingProviderEndpoint, normalizedProviderEndpoint).ConfigureAwait(false);
        await _settingsService.SetAsync(SettingsKeys.EmbeddingModel, normalizedModel).ConfigureAwait(false);
        _embeddingProviderEndpoint = NullIfEmpty(normalizedProviderEndpoint);
        _embeddingModel = NullIfEmpty(normalizedModel);

        _logger.LogInformation("Embedding configuration saved");
        OnConfigurationChanged();
    }

    /// <inheritdoc/>
    public async Task SaveLocalProcessorConfigurationAsync(
        Uri? endpoint,
        string? workingDirectory,
        string? startCommand,
        string? uploadJobSecret,
        int pdfChunkerMaxTokens,
        int csvChunkMaxTokens,
        string? pdfChunkerTokenizer) {
        if (endpoint != null && !IsValidLocalProcessorEndpoint(endpoint)) {
            throw new ArgumentException("Invalid local processor endpoint.", nameof(endpoint));
        }

        if (!string.IsNullOrWhiteSpace(workingDirectory) && !Directory.Exists(workingDirectory)) {
            throw new DirectoryNotFoundException($"The local processor directory '{workingDirectory}' was not found.");
        }

        await _settingsService.SetAsync(SettingsKeys.LocalProcessorEndpoint, endpoint?.ToString() ?? string.Empty).ConfigureAwait(false);
        await _settingsService.SetAsync(SettingsKeys.LocalProcessorWorkingDirectory, workingDirectory ?? string.Empty).ConfigureAwait(false);
        await _settingsService.SetAsync(SettingsKeys.LocalProcessorStartCommand, startCommand ?? string.Empty).ConfigureAwait(false);
        await _settingsService.SetAsync(SettingsKeys.PdfChunkerMaxTokens, pdfChunkerMaxTokens.ToString()).ConfigureAwait(false);
        await _settingsService.SetAsync(SettingsKeys.CsvChunkMaxTokens, csvChunkMaxTokens.ToString()).ConfigureAwait(false);
        await _settingsService.SetAsync(SettingsKeys.PdfChunkerTokenizer, pdfChunkerTokenizer ?? string.Empty).ConfigureAwait(false);
        await _settingsService.SetSecureAsync(SettingsKeys.LocalProcessorUploadJobSecret, uploadJobSecret ?? string.Empty).ConfigureAwait(false);

        _localProcessorEndpoint = endpoint ?? new Uri(LocalProcessorDefaults.DefaultEndpoint);
        _localProcessorWorkingDirectory = NullIfEmpty(workingDirectory);
        _localProcessorStartCommand = NullIfEmpty(startCommand) ?? LocalProcessorDefaults.DefaultStartCommand;
        _pdfChunkerMaxTokens = pdfChunkerMaxTokens <= 0 ? LocalProcessorDefaults.DefaultPdfChunkerMaxTokens : pdfChunkerMaxTokens;
        _csvChunkMaxTokens = csvChunkMaxTokens <= 0 ? LocalProcessorDefaults.DefaultCsvChunkMaxTokens : csvChunkMaxTokens;
        _pdfChunkerTokenizer = NullIfEmpty(pdfChunkerTokenizer) ?? LocalProcessorDefaults.DefaultPdfChunkerTokenizer;
        _localProcessorUploadJobSecret = NullIfEmpty(uploadJobSecret);
        RefreshLocalProcessorConfigurationState();

        _logger.LogInformation("Local processor configuration saved. IsLocalProcessorConfigured: {IsConfigured}", IsLocalProcessorConfigured);
        OnConfigurationChanged();
    }

    /// <inheritdoc/>
    public async Task ClearConfigurationAsync() {
        await _settingsService.RemoveAsync(SettingsKeys.ApiBaseUrl).ConfigureAwait(false);
        await _settingsService.RemoveSecureAsync(SettingsKeys.AuthClientId).ConfigureAwait(false);
        await _settingsService.RemoveAsync(SettingsKeys.AuthAuthority).ConfigureAwait(false);
        await _settingsService.RemoveAsync(SettingsKeys.AuthScope).ConfigureAwait(false);
        await _settingsService.RemoveAsync(SettingsKeys.EmbeddingProviderEndpoint).ConfigureAwait(false);
        await _settingsService.RemoveAsync(SettingsKeys.EmbeddingModel).ConfigureAwait(false);
        await _settingsService.RemoveAsync(SettingsKeys.LocalProcessorEndpoint).ConfigureAwait(false);
        await _settingsService.RemoveAsync(SettingsKeys.LocalProcessorWorkingDirectory).ConfigureAwait(false);
        await _settingsService.RemoveAsync(SettingsKeys.LocalProcessorStartCommand).ConfigureAwait(false);
        await _settingsService.RemoveAsync(SettingsKeys.PdfChunkerMaxTokens).ConfigureAwait(false);
        await _settingsService.RemoveAsync(SettingsKeys.CsvChunkMaxTokens).ConfigureAwait(false);
        await _settingsService.RemoveAsync(SettingsKeys.PdfChunkerTokenizer).ConfigureAwait(false);
        await _settingsService.RemoveSecureAsync(SettingsKeys.LocalProcessorUploadJobSecret).ConfigureAwait(false);

        _apiBaseUrl = null;
        _authClientId = null;
        _authAuthority = null;
        _authScope = null;
        _embeddingProviderEndpoint = null;
        _embeddingModel = null;
        _localProcessorEndpoint = new Uri(LocalProcessorDefaults.DefaultEndpoint);
        _localProcessorWorkingDirectory = LocalProcessorDefaults.TryFindWorkingDirectory();
        _localProcessorStartCommand = LocalProcessorDefaults.DefaultStartCommand;
        _pdfChunkerTokenizer = LocalProcessorDefaults.DefaultPdfChunkerTokenizer;
        _pdfChunkerMaxTokens = LocalProcessorDefaults.DefaultPdfChunkerMaxTokens;
        _csvChunkMaxTokens = LocalProcessorDefaults.DefaultCsvChunkMaxTokens;
        _localProcessorUploadJobSecret = null;
        RefreshLocalProcessorConfigurationState();

        _logger.LogInformation("Configuration cleared");
        OnConfigurationChanged();
    }

    /// <summary>
    /// Validates that the API URL is well-formed and uses appropriate protocol.
    /// Non-localhost URLs must use HTTPS.
    /// </summary>
    private static bool IsValidApiUrl(Uri uri) {
        // Allow HTTP only for localhost development
        if (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)) {
            return uri.IsLoopback;
        }

        return uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValidLocalProcessorEndpoint(Uri uri) {
        if (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)) {
            return uri.IsLoopback;
        }

        return uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns null if the string is null or whitespace, otherwise returns the string.
    /// </summary>
    private static string? NullIfEmpty(string? value) {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private void RefreshLocalProcessorConfigurationState() {
        _isLocalProcessorConfigured = _localProcessorEndpoint is not null
            && IsValidLocalProcessorEndpoint(_localProcessorEndpoint)
            && !string.IsNullOrWhiteSpace(_localProcessorWorkingDirectory)
            && Directory.Exists(_localProcessorWorkingDirectory)
            && File.Exists(Path.Combine(_localProcessorWorkingDirectory, "src", "main.py"))
            && !string.IsNullOrWhiteSpace(_localProcessorStartCommand);
    }

    /// <summary>
    /// Raises the ConfigurationChanged event.
    /// </summary>
    private void OnConfigurationChanged() {
        ConfigurationChanged?.Invoke(this, EventArgs.Empty);
    }
}
