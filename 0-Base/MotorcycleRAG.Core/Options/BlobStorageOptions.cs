namespace MotorcycleRAG.Core.Options;

/// <summary>
/// Configuration for Azure Blob Storage.
/// Binds to the "BlobStorage" section in configuration sources.
/// Values must come from Azure App Configuration or Key Vault — never hardcoded.
/// </summary>
public class BlobStorageOptions
{
    /// <summary>
    /// The Azure Storage account endpoint URI.
    /// e.g. https://&lt;account&gt;.blob.core.windows.net
    /// Set via BlobStorage:AccountEndpoint.
    /// </summary>
    public string AccountEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// Container for raw ingestion inputs awaiting processing.
    /// Defaults to "raw-uploads".
    /// </summary>
    public string RawUploadsContainer { get; set; } = "raw-uploads";

    /// <summary>
    /// Container for uploaded PDF manuals awaiting Fabric processing.
    /// Defaults to "manuals".
    /// </summary>
    public string ManualsContainer { get; set; } = "manuals";

    /// <summary>
    /// Container for uploaded CSV spec files.
    /// Defaults to "uploads".
    /// </summary>
    public string UploadsContainer { get; set; } = "uploads";
}
