namespace MotorcycleRAG.Admin.Constants;

/// <summary>
/// Defines keys for application settings stored via ISettingsService.
/// Keys are categorized by storage type (regular vs secure) and functional area.
/// </summary>
internal static class SettingsKeys {
    /// <summary>
    /// API base URL for the Motorcycle RAG backend service.
    /// Stored in regular preferences (not sensitive).
    /// </summary>
    internal const string ApiBaseUrl = "config_api_base_url";

    /// <summary>
    /// Microsoft Entra ID / Azure AD Client ID for authentication.
    /// Stored in regular preferences (public app identifier, not a secret).
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
    /// Endpoint for the embedding provider used by local processing.
    /// Stored in regular preferences (not sensitive).
    /// </summary>
    internal const string EmbeddingProviderEndpoint = "config_embedding_provider_endpoint";

    /// <summary>
    /// Selected embedding model name for local processing.
    /// Stored in regular preferences (not sensitive).
    /// </summary>
    internal const string EmbeddingModel = "config_embedding_model";

    /// <summary>
    /// Default folder path for file uploads.
    /// Stored in regular preferences (not sensitive).
    /// </summary>
    internal const string DefaultUploadPath = "config_default_upload_path";

    /// <summary>
    /// Local processor HTTP endpoint for the Python processing service.
    /// Stored in regular preferences (not sensitive).
    /// </summary>
    internal const string LocalProcessorEndpoint = "config_local_processor_endpoint";

    /// <summary>
    /// Working directory used to start the local Python processing service.
    /// Stored in regular preferences (not sensitive).
    /// </summary>
    internal const string LocalProcessorWorkingDirectory = "config_local_processor_working_directory";

    /// <summary>
    /// Maximum token count used for PDF chunking in the local processor.
    /// Stored in regular preferences (not sensitive).
    /// </summary>
    internal const string PdfChunkerMaxTokens = "config_pdf_chunker_max_tokens";

    /// <summary>
    /// Maximum token count used for CSV chunking in the local processor.
    /// Stored in regular preferences (not sensitive).
    /// </summary>
    internal const string CsvChunkMaxTokens = "config_csv_chunk_max_tokens";

    /// <summary>
    /// Tokenizer identifier used by the local processor PDF chunker.
    /// Stored in regular preferences (not sensitive).
    /// </summary>
    internal const string PdfChunkerTokenizer = "config_pdf_chunker_tokenizer";

    /// <summary>
    /// Client secret used by the local Python processing service when uploading artifacts to the API.
    /// Stored in secure storage (sensitive credential).
    /// </summary>
    internal const string LocalProcessorUploadJobSecret = "config_local_processor_upload_job_secret";
}
