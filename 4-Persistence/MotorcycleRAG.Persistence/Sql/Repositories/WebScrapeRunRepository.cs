using System;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.Persistence.Sql.Repositories;

/// <summary>
/// ADO.NET implementation of web scrape run repository
/// </summary>
public class WebScrapeRunRepository : IWebScrapeRunRepository
{
    private const string SelectColumns = @"
        [Id],
        [WebSourceId],
        [CrawlStartTime],
        [CrawlEndTime],
        [Status],
        [PagesCrawled],
        [PagesIndexed],
        [Errors],
        [ErrorMessage]";

    private readonly ISqlConnectionFactory _connectionFactory;
    private readonly ILogger<WebScrapeRunRepository> _logger;

    public WebScrapeRunRepository(
        ISqlConnectionFactory connectionFactory,
        ILogger<WebScrapeRunRepository> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<long> CreateWebScrapeRunAsync(int webSourceId)
    {
        const string sql = @"
            INSERT INTO [dbo].[WebSourceCrawlResults] (
                [WebSourceId], [CrawlStartTime], [Status]
            )
            VALUES (
                @WebSourceId, @CrawlStartTime, @Status
            );
            SELECT CAST(SCOPE_IDENTITY() AS BIGINT) AS [Id];
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var runId = await connection.QuerySingleAsync<long>(sql, new 
            { 
                WebSourceId = webSourceId,
                CrawlStartTime = DateTime.UtcNow,
                Status = (int)ScrapeRunStatus.Pending
            });

            _logger.LogInformation("Created web scrape run {RunId} for web source {WebSourceId}", runId, webSourceId);
            return runId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create web scrape run for web source {WebSourceId}", webSourceId);
            throw new InvalidOperationException($"Failed to create web scrape run for web source {webSourceId}", ex);
        }
    }

    public async Task<bool> UpdateWebScrapeRunAsync(
        long runId,
        ScrapeRunStatus status,
        int pagesCrawled,
        int pagesIndexed,
        int errors,
        string? errorMessage = null)
    {
        ValidateUpdate(status, pagesCrawled, pagesIndexed, errors, errorMessage);

        const string sql = @"
            UPDATE [dbo].[WebSourceCrawlResults] SET
                [CrawlEndTime] = @CrawlEndTime,
                [Status] = @Status,
                [PagesCrawled] = @PagesCrawled,
                [PagesIndexed] = @PagesIndexed,
                [Errors] = @Errors,
                [ErrorMessage] = @ErrorMessage
            WHERE [Id] = @RunId;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            int rowsAffected = await connection.ExecuteAsync(sql, new 
            { 
                RunId = runId,
                CrawlEndTime = IsTerminal(status) ? DateTime.UtcNow : (DateTime?)null,
                Status = (int)status,
                PagesCrawled = pagesCrawled,
                PagesIndexed = pagesIndexed,
                Errors = errors,
                ErrorMessage = errorMessage
            });

            _logger.LogInformation("Updated web scrape run {RunId} with status {Status}, rows affected: {RowsAffected}", 
                runId, status, rowsAffected);
            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update web scrape run {RunId}", runId);
            throw new InvalidOperationException($"Failed to update web scrape run {runId}", ex);
        }
    }

    public async Task<WebScrapeRun?> GetWebScrapeRunAsync(long runId)
    {
        var sql = $"SELECT {SelectColumns} FROM [dbo].[WebSourceCrawlResults] WHERE [Id] = @RunId;";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var row = await connection.QueryFirstOrDefaultAsync<WebScrapeRunRow>(sql, new { RunId = runId });
            return row is null ? null : Map(row);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get web scrape run {RunId}", runId);
            throw new InvalidOperationException($"Failed to get web scrape run {runId}", ex);
        }
    }

    public async Task<WebScrapeRun[]> GetRecentScrapeRunsAsync(int webSourceId, int limit = 10)
    {
        var sql = $@"
            SELECT TOP (@Limit) {SelectColumns} FROM [dbo].[WebSourceCrawlResults]
            WHERE [WebSourceId] = @WebSourceId
            ORDER BY [CrawlStartTime] DESC;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            return (await connection.QueryAsync<WebScrapeRunRow>(sql, new
            { 
                WebSourceId = webSourceId, 
                Limit = limit 
            })).Select(Map).ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get recent scrape runs for web source {WebSourceId}", webSourceId);
            throw new InvalidOperationException($"Failed to get recent scrape runs for web source {webSourceId}", ex);
        }
    }

    public async Task<WebScrapeRun[]> GetActiveScrapeRunsAsync()
    {
        var sql = $@"
            SELECT {SelectColumns} FROM [dbo].[WebSourceCrawlResults]
            WHERE [Status] IN (@PendingStatus, @RunningStatus)
            ORDER BY [CrawlStartTime] ASC;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            return (await connection.QueryAsync<WebScrapeRunRow>(sql, new
            {
                PendingStatus = (int)ScrapeRunStatus.Pending,
                RunningStatus = (int)ScrapeRunStatus.Running
            })).Select(Map).ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get active scrape runs");
            throw new InvalidOperationException("Failed to get active scrape runs", ex);
        }
    }

    private static WebScrapeRun Map(WebScrapeRunRow row) => WebScrapeRun.Rehydrate(
        row.Id,
        row.WebSourceId,
        row.CrawlStartTime,
        row.CrawlEndTime,
        row.Status,
        row.PagesCrawled,
        row.PagesIndexed,
        row.Errors,
        row.ErrorMessage);

    private static bool IsTerminal(ScrapeRunStatus status) => status is ScrapeRunStatus.Completed
        or ScrapeRunStatus.Failed or ScrapeRunStatus.Cancelled;

    private static void ValidateUpdate(
        ScrapeRunStatus status,
        int pagesCrawled,
        int pagesIndexed,
        int errors,
        string? errorMessage)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown scrape status.");
        }

        if (pagesCrawled < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pagesCrawled), "Scrape counters cannot be negative.");
        }

        if (pagesIndexed < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pagesIndexed), "Scrape counters cannot be negative.");
        }

        if (errors < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(errors), "Scrape counters cannot be negative.");
        }

        if (status == ScrapeRunStatus.Failed && string.IsNullOrWhiteSpace(errorMessage))
        {
            throw new ArgumentException("Failed scrape runs require an error message.", nameof(errorMessage));
        }

        if (errorMessage?.Trim().Length > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(errorMessage), "Scrape error messages cannot exceed 1000 characters.");
        }
    }

    private sealed class WebScrapeRunRow
    {
        public long Id { get; init; }
        public int WebSourceId { get; init; }
        public DateTime CrawlStartTime { get; init; }
        public DateTime? CrawlEndTime { get; init; }
        public ScrapeRunStatus Status { get; init; }
        public int PagesCrawled { get; init; }
        public int PagesIndexed { get; init; }
        public int Errors { get; init; }
        public string? ErrorMessage { get; init; }
    }
}
