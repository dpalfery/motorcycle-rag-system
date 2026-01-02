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
            throw;
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
                CrawlEndTime = DateTime.UtcNow,
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
            throw;
        }
    }

    public async Task<WebScrapeRun?> GetWebScrapeRunAsync(long runId)
    {
        const string sql = @"
            SELECT * FROM [dbo].[WebSourceCrawlResults] WHERE [Id] = @RunId;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            return await connection.QueryFirstOrDefaultAsync<WebScrapeRun>(sql, new { RunId = runId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get web scrape run {RunId}", runId);
            throw;
        }
    }

    public async Task<WebScrapeRun[]> GetRecentScrapeRunsAsync(int webSourceId, int limit = 10)
    {
        const string sql = @"
            SELECT TOP (@Limit) * FROM [dbo].[WebSourceCrawlResults]
            WHERE [WebSourceId] = @WebSourceId
            ORDER BY [CrawlStartTime] DESC;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            return (await connection.QueryAsync<WebScrapeRun>(sql, new 
            { 
                WebSourceId = webSourceId, 
                Limit = limit 
            })).ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get recent scrape runs for web source {WebSourceId}", webSourceId);
            throw;
        }
    }

    public async Task<WebScrapeRun[]> GetActiveScrapeRunsAsync()
    {
        const string sql = @"
            SELECT * FROM [dbo].[WebSourceCrawlResults]
            WHERE [Status] IN (@PendingStatus, @RunningStatus)
            ORDER BY [CrawlStartTime] ASC;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            return (await connection.QueryAsync<WebScrapeRun>(sql, new
            {
                PendingStatus = (int)ScrapeRunStatus.Pending,
                RunningStatus = (int)ScrapeRunStatus.Running
            })).ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get active scrape runs");
            throw;
        }
    }
}
