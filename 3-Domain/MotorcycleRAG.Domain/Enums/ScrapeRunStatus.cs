namespace MotorcycleRAG.Domain.Enums;

/// <summary>
/// Represents the status of a web scrape run operation
/// </summary>
public enum ScrapeRunStatus
{
    /// <summary>
    /// Scrape operation is pending to start
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Scrape operation is currently running
    /// </summary>
    Running = 1,

    /// <summary>
    /// Scrape operation has completed successfully
    /// </summary>
    Completed = 2,

    /// <summary>
    /// Scrape operation failed with errors
    /// </summary>
    Failed = 3,

    /// <summary>
    /// Scrape operation was cancelled by user
    /// </summary>
    Cancelled = 4
}
