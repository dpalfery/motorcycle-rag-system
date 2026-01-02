using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Domain.Entities;

/// <summary>
/// Represents a web scrape/index run for a web source
/// </summary>
public class WebScrapeRun
{
    /// <summary>
    /// Unique identifier for the scrape run
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// The web source being scraped
    /// </summary>
    [Required]
    public int WebSourceId { get; set; }

    /// <summary>
    /// When the scraping started
    /// </summary>
    public DateTime CrawlStartTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the scraping completed (null if still running)
    /// </summary>
    public DateTime? CrawlEndTime { get; set; }

    /// <summary>
    /// Current status of the scrape run
    /// </summary>
    [Required]
    public string Status { get; set; } = "Pending";

    /// <summary>
    /// Number of pages successfully crawled
    /// </summary>
    public int PagesCrawled { get; set; }

    /// <summary>
    /// Number of pages successfully indexed
    /// </summary>
    public int PagesIndexed { get; set; }

    /// <summary>
    /// Number of errors encountered during scraping
    /// </summary>
    public int Errors { get; set; }

    /// <summary>
    /// Error message if the scrape run failed
    /// </summary>
    [StringLength(1000)]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Calculate duration of the scrape run
    /// </summary>
    public TimeSpan? Duration => CrawlEndTime.HasValue ? 
        CrawlEndTime.Value - CrawlStartTime : null;

    /// <summary>
    /// Check if the run is currently running
    /// </summary>
    public bool IsRunning => Status == "Running";

    /// <summary>
    /// Check if the run completed successfully
    /// </summary>
    public bool IsCompleted => Status == "Completed";

    /// <summary>
    /// Check if the run failed
    /// </summary>
    public bool IsFailed => Status == "Failed";

    /// <summary>
    /// Check if the run is still pending
    /// </summary>
    public bool IsPending => Status == "Pending";
}
