namespace MotorcycleRAG.Admin.Services;

/// <summary>
/// Tracks the application's configuration state and provides access to configuration values.
/// This service loads settings from ISettingsService on startup and notifies when configuration changes.
/// </summary>
internal interface IConfigurationStateService
{
    /// <summary>
    /// Gets a value indicating whether the application is fully configured.
    /// Returns true when both API and Auth are configured.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Gets a value indicating whether the API URL is configured and valid.
    /// </summary>
    bool IsApiConfigured { get; }

    /// <summary>
    /// Gets a value indicating whether authentication credentials are configured.
    /// </summary>
    bool IsAuthConfigured { get; }

    /// <summary>
    /// Gets the configured API base URL, or null if not configured.
    /// </summary>
    Uri? ApiBaseUrl { get; }

    /// <summary>
    /// Gets the configured authentication client ID, or null if not configured.
    /// </summary>
    string? AuthClientId { get; }

    /// <summary>
    /// Gets the configured authentication authority URL, or null if not configured.
    /// </summary>
    string? AuthAuthority { get; }

    /// <summary>
    /// Gets the configured authentication scope, or null if not configured.
    /// </summary>
    string? AuthScope { get; }

    /// <summary>
    /// Gets the configured embedding model path, or null if not configured.
    /// </summary>
    string? EmbeddingModelPath { get; }

    /// <summary>
    /// Gets the configured local processor endpoint, or the in-app default if none was saved.
    /// </summary>
    Uri? LocalProcessorEndpoint { get; }

    /// <summary>
    /// Gets the configured local processor working directory, or the discovered default if available.
    /// </summary>
    string? LocalProcessorWorkingDirectory { get; }

    /// <summary>
    /// Gets the configured local processor start command, or the in-app default if none was saved.
    /// </summary>
    string? LocalProcessorStartCommand { get; }

    /// <summary>
    /// Gets a value indicating whether the local processor can be started from the admin app.
    /// </summary>
    bool IsLocalProcessorConfigured { get; }

    /// <summary>
    /// Loads configuration values from settings storage.
    /// Should be called during app startup.
    /// </summary>
    Task LoadConfigurationAsync();

    /// <summary>
    /// Saves the API base URL to settings.
    /// </summary>
    /// <param name="url">The API base URL to save</param>
    Task SaveApiBaseUrlAsync(Uri? url);

    /// <summary>
    /// Saves authentication configuration to settings.
    /// </summary>
    /// <param name="clientId">The Azure AD client ID</param>
    /// <param name="authority">The Azure AD authority URL</param>
    /// <param name="scope">The API scope for token requests</param>
    Task SaveAuthConfigurationAsync(string clientId, string authority, string scope);

    /// <summary>
    /// Saves the embedding model path to settings.
    /// </summary>
    /// <param name="path">The path to the ONNX embedding model</param>
    Task SaveEmbeddingModelPathAsync(string path);

    /// <summary>
    /// Saves local processor startup configuration to settings.
    /// </summary>
    /// <param name="endpoint">The local processor endpoint.</param>
    /// <param name="workingDirectory">The local processor working directory.</param>
    /// <param name="startCommand">The command used to start the processor.</param>
    Task SaveLocalProcessorConfigurationAsync(Uri? endpoint, string? workingDirectory, string? startCommand);

    /// <summary>
    /// Clears all configuration settings.
    /// </summary>
    Task ClearConfigurationAsync();

    /// <summary>
    /// Occurs when configuration changes.
    /// </summary>
    event EventHandler? ConfigurationChanged;
}
