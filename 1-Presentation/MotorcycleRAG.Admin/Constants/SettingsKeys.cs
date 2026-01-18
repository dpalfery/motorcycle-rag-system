namespace MotorcycleRAG.Admin.Constants;

/// <summary>
/// Defines keys for application settings stored via ISettingsService.
/// Keys are categorized by storage type (regular vs secure) and functional area.
/// </summary>
internal static class SettingsKeys
{
    /// <summary>
    /// API base URL for the Motorcycle RAG backend service.
    /// Stored in regular preferences (not sensitive).
    /// </summary>
    internal const string ApiBaseUrl = "config_api_base_url";

    /// <summary>
    /// Microsoft Entra ID / Azure AD Client ID for authentication.
    /// Stored in secure storage (sensitive credential).
    /// </summary>
    internal const string AuthClientId = "config_auth_client_id";

    /// <summary>
    /// Microsoft Entra ID / Azure AD Authority URL.
    /// Stored in regular preferences (not sensitive).
    /// </summary>
    internal const string AuthAuthority = "config_auth_authority";

    /// <summary>
    /// API scope for authentication token requests.
    /// Stored in regular preferences (not sensitive).
    /// </summary>
    internal const string AuthScope = "config_auth_scope";

    /// <summary>
    /// Path to the ONNX embedding model file for local processing.
    /// Stored in regular preferences (not sensitive).
    /// </summary>
    internal const string EmbeddingModelPath = "config_embedding_model_path";

    /// <summary>
    /// Default folder path for file uploads.
    /// Stored in regular preferences (not sensitive).
    /// </summary>
    internal const string DefaultUploadPath = "config_default_upload_path";
}
