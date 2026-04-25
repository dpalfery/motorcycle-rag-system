namespace MotorcycleRAG.Core.Options;

/// <summary>
/// Configuration for Microsoft Fabric ingestion pipeline integration.
/// Binds to the "FabricIngestion" section in configuration sources.
/// .NET values are provided through Azure App Configuration and Key Vault where needed.
/// </summary>
#pragma warning disable S1133 // Intentionally kept as deprecated fallback
[Obsolete("Use IngestionOptions with ProcessingMode.Fabric. FabricIngestionOptions kept as fallback.", false)]
#pragma warning restore S1133
public class FabricIngestionOptions
{
    /// <summary>
    /// The Microsoft Fabric REST API workspace endpoint.
    /// Set via FabricIngestion:WorkspaceEndpoint.
    /// </summary>
    public string WorkspaceEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// The Fabric pipeline item ID to trigger for PDF manual ingestion.
    /// Set via FabricIngestion:PdfPipelineId.
    /// </summary>
    public string PdfPipelineId { get; set; } = string.Empty;

    /// <summary>
    /// The Fabric pipeline item ID to trigger for CSV spec dataset ingestion.
    /// Set via FabricIngestion:CsvPipelineId.
    /// </summary>
    public string CsvPipelineId { get; set; } = string.Empty;

    /// <summary>
    /// Timeout in minutes for a single Fabric pipeline run before the job is marked as timed out.
    /// Defaults to 360 minutes (6 hours) per workload limits spec.
    /// </summary>
    public int PipelineTimeoutMinutes { get; set; } = 360;

    /// <summary>
    /// Polling interval in seconds for checking Fabric run status.
    /// </summary>
    public int StatusPollingIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum file size in bytes accepted for manual PDF uploads.
    /// 2 GB per workload limits spec.
    /// </summary>
    public long MaxInputBytes { get; set; } = 2_000_000_000L;

    /// <summary>
    /// Maximum page count accepted for manual PDF uploads.
    /// </summary>
    public int MaxPages { get; set; } = 2000;

    /// <summary>
    /// Monthly Azure spend limit in the subscription currency (e.g. USD).
    /// When the estimated spend exceeds this value, new ingestion jobs may be rejected.
    /// Set via FabricIngestion:MonthlyBudgetLimit.
    /// A value of 0 disables budget enforcement.
    /// </summary>
    public decimal MonthlyBudgetLimit { get; set; } = 500m;
}
