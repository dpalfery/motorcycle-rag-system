using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models;
using MotorcycleRAG.Domain.Models;

namespace MotorcycleRAG.Persistence.Sql.Repositories
{
    /// <summary>
    /// ADO.NET implementation of web source repository
    /// </summary>
    public class WebSourceRepository : IWebSourceRepository
    {
        private readonly ISqlConnectionFactory _connectionFactory;
        private readonly ILogger<WebSourceRepository> _logger;

        /// <summary>
        /// Initializes a new instance of the WebSourceRepository
        /// </summary>
        /// <param name="connectionFactory">SQL connection factory</param>
        /// <param name="logger">Logger</param>
        public WebSourceRepository(ISqlConnectionFactory connectionFactory, ILogger<WebSourceRepository> logger)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Creates a new web source
        /// </summary>
        /// <param name="webSource">Web source to create</param>
        /// <returns>Created web source</returns>
        public async Task<WebSource> CreateWebSourceAsync(WebSource webSource)
        {
            if (webSource == null)
            {
                throw new ArgumentNullException(nameof(webSource));
            }

            const string sql = @"
                INSERT INTO [dbo].[WebSources] (
                    [Url], [Name], [Description], [IsEnabled], [TrustTier],
                    [CreatedDate], [LastUpdatedDate], [LastCrawledDate], [CrawlFrequencyHours],
                    [IncludeInSearch], [MaxCrawlDepth]
                )
                VALUES (
                    @Url, @Name, @Description, @IsEnabled, @TrustTier,
                    @CreatedDate, @LastUpdatedDate, @LastCrawledDate, @CrawlFrequencyHours,
                    @IncludeInSearch, @MaxCrawlDepth
                );
                SELECT CAST(SCOPE_IDENTITY() AS INT) AS [Id];
            ";

            try
            {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                using var transaction = connection.BeginTransaction();
                
                try
                {
                    var webSourceId = await connection.QueryFirstOrDefaultAsync<int>(sql, webSource, transaction);
                    webSource.Id = webSourceId;
                    
                    transaction.Commit();
                    _logger.LogInformation("Created web source with ID {WebSourceId}", webSourceId);
                    return webSource;
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create web source");
                throw;
            }
        }

        /// <summary>
        /// Gets a web source by ID
        /// </summary>
        /// <param name="webSourceId">Web source ID</param>
        /// <returns>Web source if found, null otherwise</returns>
        public async Task<WebSource?> GetWebSourceByIdAsync(int webSourceId)
        {
            const string sql = @"
                SELECT * FROM [dbo].[WebSources] WHERE [Id] = @WebSourceId;
            ";

            try
            {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                return await connection.QueryFirstOrDefaultAsync<WebSource>(sql, new { WebSourceId = webSourceId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get web source by ID {WebSourceId}", webSourceId);
                throw;
            }
        }

        /// <summary>
        /// Gets all web sources
        /// </summary>
        /// <returns>List of all web sources</returns>
        public async Task<WebSource[]> GetAllWebSourcesAsync()
        {
            const string sql = @"
                SELECT * FROM [dbo].[WebSources] ORDER BY [Name];
            ";

            try
            {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                return (await connection.QueryAsync<WebSource>(sql)).ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get all web sources");
                throw;
            }
        }

        /// <summary>
        /// Updates an existing web source
        /// </summary>
        /// <param name="webSource">Web source to update</param>
        /// <returns>True if successful, false otherwise</returns>
        public async Task<bool> UpdateWebSourceAsync(WebSource webSource)
        {
            if (webSource == null)
            {
                throw new ArgumentNullException(nameof(webSource));
            }

            const string sql = @"
                UPDATE [dbo].[WebSources] SET
                    [Url] = @Url,
                    [Name] = @Name,
                    [Description] = @Description,
                    [IsEnabled] = @IsEnabled,
                    [TrustTier] = @TrustTier,
                    [LastUpdatedDate] = @LastUpdatedDate,
                    [LastCrawledDate] = @LastCrawledDate,
                    [CrawlFrequencyHours] = @CrawlFrequencyHours,
                    [IncludeInSearch] = @IncludeInSearch,
                    [MaxCrawlDepth] = @MaxCrawlDepth
                WHERE [Id] = @Id;
            ";

            try
            {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                int rowsAffected = await connection.ExecuteAsync(sql, webSource);
                
                _logger.LogInformation("Updated web source with ID {WebSourceId}, rows affected: {RowsAffected}", 
                    webSource.Id, rowsAffected);
                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update web source with ID {WebSourceId}", webSource.Id);
                throw;
            }
        }

        /// <summary>
        /// Deletes a web source
        /// </summary>
        /// <param name="webSourceId">Web source ID</param>
        /// <returns>True if successful, false otherwise</returns>
        public async Task<bool> DeleteWebSourceAsync(int webSourceId)
        {
            const string sql = @"
                DELETE FROM [dbo].[WebSources] WHERE [Id] = @WebSourceId;
            ";

            try
            {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                int rowsAffected = await connection.ExecuteAsync(sql, new { WebSourceId = webSourceId });
                
                _logger.LogInformation("Deleted web source with ID {WebSourceId}, rows affected: {RowsAffected}", 
                    webSourceId, rowsAffected);
                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete web source with ID {WebSourceId}", webSourceId);
                throw;
            }
        }

        /// <summary>
        /// Gets web source by URL
        /// </summary>
        /// <param name="url">URL to search for</param>
        /// <returns>Web source if found, null otherwise</returns>
        public async Task<WebSource?> GetWebSourceByUrlAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException("URL cannot be null or empty", nameof(url));
            }

            const string sql = @"
                SELECT * FROM [dbo].[WebSources] WHERE [Url] = @Url;
            ";

            try
            {
                using var connection = await _connectionFactory.CreateOpenConnectionAsync();
                return await connection.QueryFirstOrDefaultAsync<WebSource>(sql, new { Url = url });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get web source by URL {Url}", url);
                throw;
            }
        }
    }
}
