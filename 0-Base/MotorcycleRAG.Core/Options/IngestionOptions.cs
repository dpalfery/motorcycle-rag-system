namespace MotorcycleRAG.Core.Options;

/// <summary>
/// Configuration for the ingestion pipeline. Binds to the "Ingestion" section.
/// All values must be provided via environment variables — never hardcoded.
/// </summary>
public class IngestionOptions
{
    /// <summary>
    /// Selects whether to use the local Python FastAPI service or the Microsoft Fabric pipeline.
    /// Set via MCR_API_INGESTION_MODE environment variable.
    /// </summary>
    public ProcessingMode Mode { get; set; } = ProcessingMode.Local;

    /// <summary>
    /// The base URL of the local Python FastAPI document-processing service.
    /// Set via MCR_API_INGESTION_LOCAL_ENDPOINT environment variable.
    /// </summary>
    public string LocalEndpoint { get; set; } = "http://localhost:8100";

    /// <summary>
    /// The Microsoft Fabric REST API workspace endpoint.
    /// Set via MCR_API_FABRIC_WORKSPACE_ENDPOINT environment variable.
    /// </summary>
    public string WorkspaceEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// The Fabric pipeline item ID to trigger for PDF manual ingestion.
    /// Set via MCR_API_FABRIC_PDF_PIPELINE_ID environment variable.
    /// </summary>
    public string PdfPipelineId { get; set; } = string.Empty;

    /// <summary>
    /// The Fabric pipeline item ID to trigger for CSV spec dataset ingestion.
    /// Set via MCR_API_FABRIC_CSV_PIPELINE_ID environment variable.
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
    /// Set via MCR_API_FABRIC_MONTHLY_BUDGET_LIMIT environment variable.
    /// A value of 0 disables budget enforcement.
    /// </summary>
    public decimal MonthlyBudgetLimit { get; set; } = 500m;
}
