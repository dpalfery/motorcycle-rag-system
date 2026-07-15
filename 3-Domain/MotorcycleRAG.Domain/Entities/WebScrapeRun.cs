using System.ComponentModel.DataAnnotations;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.Domain.Entities;

/// <summary>
/// Represents a web scrape/index run for a web source
/// </summary>
public class WebScrapeRun
{
    private WebScrapeRun(
        long id,
        int webSourceId,
        DateTime crawlStartTime,
        DateTime? crawlEndTime,
        ScrapeRunStatus status,
        int pagesCrawled,
        int pagesIndexed,
        int errors,
        string? errorMessage)
    {
        Id = id;
        WebSourceId = webSourceId;
        CrawlStartTime = crawlStartTime;
        CrawlEndTime = crawlEndTime;
        Status = status;
        PagesCrawled = pagesCrawled;
        PagesIndexed = pagesIndexed;
        Errors = errors;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// Unique identifier for the scrape run
    /// </summary>
    public long Id { get; }

    /// <summary>
    /// The web source being scraped
    /// </summary>
    [Required]
    public int WebSourceId { get; }

    /// <summary>
    /// When the scraping started
    /// </summary>
    public DateTime CrawlStartTime { get; }

    /// <summary>
    /// When the scraping completed (null if still running)
    /// </summary>
    public DateTime? CrawlEndTime { get; private set; }

    /// <summary>
    /// Current status of the scrape run
    /// </summary>
    [Required]
    public ScrapeRunStatus Status { get; private set; }

    /// <summary>
    /// Number of pages successfully crawled
    /// </summary>
    public int PagesCrawled { get; private set; }

    /// <summary>
    /// Number of pages successfully indexed
    /// </summary>
    public int PagesIndexed { get; private set; }

    /// <summary>
    /// Number of errors encountered during scraping
    /// </summary>
    public int Errors { get; private set; }

    /// <summary>
    /// Error message if the scrape run failed
    /// </summary>
    [StringLength(1000)]
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// Calculate duration of the scrape run
    /// </summary>
    public TimeSpan? Duration => CrawlEndTime.HasValue ? 
        CrawlEndTime.Value - CrawlStartTime : null;

    /// <summary>
    /// Check if the run is currently running
    /// </summary>
    public bool IsRunning => Status == ScrapeRunStatus.Running;

    /// <summary>
    /// Check if the run completed successfully
    /// </summary>
    public bool IsCompleted => Status == ScrapeRunStatus.Completed;

    /// <summary>
    /// Check if the run failed
    /// </summary>
    public bool IsFailed => Status == ScrapeRunStatus.Failed;

    /// <summary>
    /// Check if the run is still pending
    /// </summary>
    public bool IsPending => Status == ScrapeRunStatus.Pending;

    public bool IsCancelled => Status == ScrapeRunStatus.Cancelled;

    /// <summary>Creates a pending run for a valid web source.</summary>
    public static WebScrapeRun Create(int webSourceId, DateTime? startedAtUtc = null)
    {
        return CreateInternal(
            id: 0,
            webSourceId,
            startedAtUtc?.ToUniversalTime() ?? DateTime.UtcNow,
            crawlEndTime: null,
            ScrapeRunStatus.Pending,
            pagesCrawled: 0,
            pagesIndexed: 0,
            errors: 0,
            errorMessage: null,
            nameof(webSourceId));
    }

    /// <summary>
    /// Rehydrates a persisted run after validating the complete lifecycle state at the
    /// Persistence boundary. Invalid database rows are rejected rather than becoming a
    /// partially-valid domain entity.
    /// </summary>
    public static WebScrapeRun Rehydrate(
        long id,
        int webSourceId,
        DateTime crawlStartTime,
        DateTime? crawlEndTime,
        ScrapeRunStatus status,
        int pagesCrawled,
        int pagesIndexed,
        int errors,
        string? errorMessage) =>
        CreateInternal(
            id,
            webSourceId,
            crawlStartTime,
            crawlEndTime,
            status,
            pagesCrawled,
            pagesIndexed,
            errors,
            errorMessage,
            nameof(webSourceId));

    /// <summary>Moves a pending run into the running state.</summary>
    public void Start()
    {
        EnsureStatus(ScrapeRunStatus.Pending);
        Status = ScrapeRunStatus.Running;
    }

    /// <summary>Completes a running run and records its counters.</summary>
    public void Complete(int pagesCrawled, int pagesIndexed, int errors = 0, DateTime? completedAtUtc = null)
    {
        EnsureStatus(ScrapeRunStatus.Running);
        SetCounters(pagesCrawled, pagesIndexed, errors);
        ErrorMessage = null;
        CrawlEndTime = completedAtUtc?.ToUniversalTime() ?? DateTime.UtcNow;
        Status = ScrapeRunStatus.Completed;
    }

    /// <summary>Fails a pending or running run with a safe diagnostic message.</summary>
    public void Fail(string errorMessage, int pagesCrawled = 0, int pagesIndexed = 0, int errors = 1, DateTime? failedAtUtc = null)
    {
        if (!IsPending && !IsRunning)
        {
            throw new InvalidOperationException($"A {Status} scrape run cannot fail.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        SetCounters(pagesCrawled, pagesIndexed, errors);
        ErrorMessage = NormalizeError(errorMessage);
        CrawlEndTime = failedAtUtc?.ToUniversalTime() ?? DateTime.UtcNow;
        Status = ScrapeRunStatus.Failed;
    }

    /// <summary>Cancels a running scrape and records the partial counters.</summary>
    public void Cancel(int pagesCrawled = 0, int pagesIndexed = 0, int errors = 0, DateTime? cancelledAtUtc = null)
    {
        EnsureStatus(ScrapeRunStatus.Running);
        SetCounters(pagesCrawled, pagesIndexed, errors);
        ErrorMessage = "Scrape operation was cancelled by user";
        CrawlEndTime = cancelledAtUtc?.ToUniversalTime() ?? DateTime.UtcNow;
        Status = ScrapeRunStatus.Cancelled;
    }

    private void SetCounters(int pagesCrawled, int pagesIndexed, int errors)
    {
        PagesCrawled = ValidateCount(pagesCrawled, nameof(pagesCrawled));
        PagesIndexed = ValidateCount(pagesIndexed, nameof(pagesIndexed));
        Errors = ValidateCount(errors, nameof(errors));
    }

    private void EnsureStatus(ScrapeRunStatus expected)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException($"A {Status} scrape run cannot transition to {expected}.");
        }
    }

    private static int ValidateCount(int value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Counters cannot be negative.");
        }

        return value;
    }

    private static string? NormalizeError(string? errorMessage)
        => string.IsNullOrWhiteSpace(errorMessage) ? null : errorMessage.Trim()[..Math.Min(errorMessage.Trim().Length, 1000)];

    private static WebScrapeRun CreateInternal(
        long id,
        int webSourceId,
        DateTime crawlStartTime,
        DateTime? crawlEndTime,
        ScrapeRunStatus status,
        int pagesCrawled,
        int pagesIndexed,
        int errors,
        string? errorMessage,
        string webSourceIdParameterName)
    {
        if (id < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id), id, "Run ID cannot be negative.");
        }

        if (webSourceId <= 0)
        {
            throw new ArgumentOutOfRangeException(webSourceIdParameterName, webSourceId, "Web source ID must be positive.");
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown scrape status.");
        }

        ValidateCount(pagesCrawled, nameof(pagesCrawled));
        ValidateCount(pagesIndexed, nameof(pagesIndexed));
        ValidateCount(errors, nameof(errors));

        var isTerminal = status is ScrapeRunStatus.Completed or ScrapeRunStatus.Failed or ScrapeRunStatus.Cancelled;
        if (isTerminal != crawlEndTime.HasValue)
        {
            throw new ArgumentException("Terminal scrape runs must have an end time, and active runs must not.", nameof(crawlEndTime));
        }

        if (errorMessage?.Trim().Length > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(errorMessage), "Scrape error messages cannot exceed 1000 characters.");
        }

        var normalizedError = NormalizeError(errorMessage);
        if (status == ScrapeRunStatus.Failed && normalizedError is null)
        {
            throw new ArgumentException("Failed scrape runs require an error message.", nameof(errorMessage));
        }

        return new WebScrapeRun(
            id,
            webSourceId,
            crawlStartTime.ToUniversalTime(),
            crawlEndTime?.ToUniversalTime(),
            status,
            pagesCrawled,
            pagesIndexed,
            errors,
            normalizedError);
    }
}
