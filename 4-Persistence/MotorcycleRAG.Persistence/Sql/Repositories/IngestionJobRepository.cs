using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models;
using MotorcycleRAG.Domain.Models;

namespace MotorcycleRAG.Persistence.Sql.Repositories;

/// <summary>
/// Dapper-based implementation of ingestion job repository
/// </summary>
public class IngestionJobRepository : IIngestionJobRepository
{
    private readonly ISqlConnectionFactory _connectionFactory;
    private readonly ILogger<IngestionJobRepository> _logger;

    /// <summary>
    /// Initializes a new instance of the IngestionJobRepository
    /// </summary>
    /// <param name="connectionFactory">SQL connection factory</param>
    /// <param name="logger">Logger</param>
    public IngestionJobRepository(ISqlConnectionFactory connectionFactory, ILogger<IngestionJobRepository> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates a new ingestion job record
    /// </summary>
    public async Task<IngestionJob> CreateAsync(IngestionJob job)
    {
        if (job == null)
        {
            throw new ArgumentNullException(nameof(job));
        }

        const string sql = @"
            INSERT INTO [dbo].[IngestionJobs] (
                [JobId], [JobType], [Status], [SourceFilePath], [SourceFileName],
                [StartTime], [UserId], [UserEmail], [TotalRecordsProcessed],
                [RecordsIndexed], [RecordsFailed], [RecordsWithWarnings],
                [MetricsJson], [ErrorsJson], [ErrorMessage], [MetadataJson], [CreatedAt]
            )
            VALUES (
                @JobId, @JobType, @Status, @SourceFilePath, @SourceFileName,
                @StartTime, @UserId, @UserEmail, @TotalRecordsProcessed,
                @RecordsIndexed, @RecordsFailed, @RecordsWithWarnings,
                @MetricsJson, @ErrorsJson, @ErrorMessage, @MetadataJson, @CreatedAt
            );
            SELECT CAST(SCOPE_IDENTITY() AS BIGINT) AS [Id];
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            using var transaction = connection.BeginTransaction();
            
            try
            {
                var parameters = new
                {
                    JobId = job.JobId,
                    JobType = job.JobType.ToString(),
                    Status = job.Status.ToString(),
                    SourceFilePath = job.SourceFilePath,
                    SourceFileName = job.SourceFileName,
                    StartTime = job.StartTime,
                    UserId = job.UserId,
                    UserEmail = job.UserEmail,
                    TotalRecordsProcessed = job.TotalRecordsProcessed,
                    RecordsIndexed = job.RecordsIndexed,
                    RecordsFailed = job.RecordsFailed,
                    RecordsWithWarnings = job.RecordsWithWarnings,
                    MetricsJson = job.MetricsJson,
                    ErrorsJson = job.ErrorsJson,
                    ErrorMessage = job.ErrorMessage,
                    MetadataJson = job.MetadataJson,
                    CreatedAt = job.CreatedAt
                };

                var jobId = await connection.QueryFirstOrDefaultAsync<long>(sql, parameters, transaction);
                job.Id = jobId;
                
                transaction.Commit();
                _logger.LogDebug("Created ingestion job with ID {JobId} for source {SourcePath}", 
                    jobId, SanitizeForLog(job.SourceFilePath));
                return job;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create ingestion job for source {SourcePath}", 
                SanitizeForLog(job.SourceFilePath));
            throw;
        }
    }

    /// <summary>
    /// Gets an ingestion job by its ID
    /// </summary>
    public async Task<IngestionJob?> GetByIdAsync(long id)
    {
        if (id <= 0)
        {
            throw new ArgumentException("ID must be greater than 0", nameof(id));
        }

        const string sql = @"
            SELECT * FROM [dbo].[IngestionJobs]
            WHERE [Id] = @Id;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var job = await connection.QueryFirstOrDefaultAsync<IngestionJob>(sql, new { Id = id });
            return job;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get ingestion job by ID {JobId}", id);
            throw;
        }
    }

    /// <summary>
    /// Gets an ingestion job by its unique job identifier
    /// </summary>
    public async Task<IngestionJob?> GetByJobIdAsync(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("Job ID cannot be null or empty", nameof(jobId));
        }

        const string sql = @"
            SELECT * FROM [dbo].[IngestionJobs]
            WHERE [JobId] = @JobId;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var job = await connection.QueryFirstOrDefaultAsync<IngestionJob>(sql, new { JobId = jobId });
            return job;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get ingestion job by JobId {JobId}", SanitizeForLog(jobId));
            throw;
        }
    }

    /// <summary>
    /// Updates an existing ingestion job
    /// </summary>
    public async Task<IngestionJob> UpdateAsync(IngestionJob job)
    {
        if (job == null)
        {
            throw new ArgumentNullException(nameof(job));
        }

        if (job.Id <= 0)
        {
            throw new ArgumentException("Job ID must be greater than 0", nameof(job.Id));
        }

        const string sql = @"
            UPDATE [dbo].[IngestionJobs]
            SET 
                [JobType] = @JobType,
                [Status] = @Status,
                [SourceFilePath] = @SourceFilePath,
                [SourceFileName] = @SourceFileName,
                [StartTime] = @StartTime,
                [EndTime] = @EndTime,
                [UserId] = @UserId,
                [UserEmail] = @UserEmail,
                [TotalRecordsProcessed] = @TotalRecordsProcessed,
                [RecordsIndexed] = @RecordsIndexed,
                [RecordsFailed] = @RecordsFailed,
                [RecordsWithWarnings] = @RecordsWithWarnings,
                [MetricsJson] = @MetricsJson,
                [ErrorsJson] = @ErrorsJson,
                [ErrorMessage] = @ErrorMessage,
                [MetadataJson] = @MetadataJson,
                [UpdatedAt] = @UpdatedAt
            WHERE [Id] = @Id;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            using var transaction = connection.BeginTransaction();
            
            try
            {
                var parameters = new
                {
                    Id = job.Id,
                    JobType = job.JobType.ToString(),
                    Status = job.Status.ToString(),
                    SourceFilePath = job.SourceFilePath,
                    SourceFileName = job.SourceFileName,
                    StartTime = job.StartTime,
                    EndTime = job.EndTime,
                    UserId = job.UserId,
                    UserEmail = job.UserEmail,
                    TotalRecordsProcessed = job.TotalRecordsProcessed,
                    RecordsIndexed = job.RecordsIndexed,
                    RecordsFailed = job.RecordsFailed,
                    RecordsWithWarnings = job.RecordsWithWarnings,
                    MetricsJson = job.MetricsJson,
                    ErrorsJson = job.ErrorsJson,
                    ErrorMessage = job.ErrorMessage,
                    MetadataJson = job.MetadataJson,
                    UpdatedAt = DateTime.UtcNow
                };

                var rowsAffected = await connection.ExecuteAsync(sql, parameters, transaction);
                
                if (rowsAffected == 0)
                {
                    throw new InvalidOperationException($"Ingestion job with ID {job.Id} not found");
                }
                
                transaction.Commit();
                _logger.LogDebug("Updated ingestion job with ID {JobId}", job.Id);
                return job;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update ingestion job with ID {JobId}", job.Id);
            throw;
        }
    }

    /// <summary>
    /// Updates status of an ingestion job
    /// </summary>
    public async Task<bool> UpdateStatusAsync(string jobId, IngestionJobStatus status, DateTime? endTime = null, string? errorMessage = null)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("Job ID cannot be null or empty", nameof(jobId));
        }

        const string sql = @"
            UPDATE [dbo].[IngestionJobs]
            SET 
                [Status] = @Status,
                [EndTime] = @EndTime,
                [ErrorMessage] = @ErrorMessage,
                [UpdatedAt] = @UpdatedAt
            WHERE [JobId] = @JobId;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var rowsAffected = await connection.ExecuteAsync(sql, new
            {
                JobId = jobId,
                Status = status.ToString(),
                EndTime = endTime,
                ErrorMessage = errorMessage,
                UpdatedAt = DateTime.UtcNow
            });

            _logger.LogDebug("Updated status for ingestion job {JobId} to {Status}", SanitizeForLog(jobId), status);
            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update status for ingestion job {JobId}", SanitizeForLog(jobId));
            throw;
        }
    }

    /// <summary>
    /// Updates metrics of an ingestion job
    /// </summary>
    public async Task<bool> UpdateMetricsAsync(
        string jobId,
        int totalRecordsProcessed,
        int recordsIndexed,
        int recordsFailed = 0,
        int recordsWithWarnings = 0)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("Job ID cannot be null or empty", nameof(jobId));
        }

        const string sql = @"
            UPDATE [dbo].[IngestionJobs]
            SET 
                [TotalRecordsProcessed] = @TotalRecordsProcessed,
                [RecordsIndexed] = @RecordsIndexed,
                [RecordsFailed] = @RecordsFailed,
                [RecordsWithWarnings] = @RecordsWithWarnings,
                [UpdatedAt] = @UpdatedAt
            WHERE [JobId] = @JobId;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var rowsAffected = await connection.ExecuteAsync(sql, new
            {
                JobId = jobId,
                TotalRecordsProcessed = totalRecordsProcessed,
                RecordsIndexed = recordsIndexed,
                RecordsFailed = recordsFailed,
                RecordsWithWarnings = recordsWithWarnings,
                UpdatedAt = DateTime.UtcNow
            });

            _logger.LogDebug("Updated metrics for ingestion job {JobId}: {Total} total, {Indexed} indexed, {Failed} failed, {Warnings} warnings",
                SanitizeForLog(jobId), totalRecordsProcessed, recordsIndexed, recordsFailed, recordsWithWarnings);
            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update metrics for ingestion job {JobId}", SanitizeForLog(jobId));
            throw;
        }
    }

    /// <summary>
    /// Adds an error to an ingestion job
    /// </summary>
    public async Task<bool> AddErrorAsync(string jobId, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("Job ID cannot be null or empty", nameof(jobId));
        }

        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            throw new ArgumentException("Error message cannot be null or empty", nameof(errorMessage));
        }

        try
        {
            var job = await GetByJobIdAsync(jobId);
            if (job == null)
            {
                _logger.LogWarning("Ingestion job {JobId} not found when adding error", SanitizeForLog(jobId));
                return false;
            }

            var errors = job.GetErrors();
            errors.Add(errorMessage);
            job.SetErrors(errors);
            job.RecordsFailed++;
            job.ErrorMessage = errorMessage;

            await UpdateAsync(job);
            _logger.LogDebug("Added error to ingestion job {JobId}", SanitizeForLog(jobId));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add error to ingestion job {JobId}", SanitizeForLog(jobId));
            throw;
        }
    }

    /// <summary>
    /// Gets ingestion jobs by status
    /// </summary>
    public async Task<IngestionJob[]> GetByStatusAsync(IngestionJobStatus status, int limit = 100)
    {
        if (limit <= 0)
        {
            throw new ArgumentException("Limit must be greater than 0", nameof(limit));
        }

        const string sql = @"
            EXEC [dbo].[sp_GetIngestionJobsByStatus]
                @Status = @Status,
                @Limit = @Limit;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var jobs = await connection.QueryAsync<IngestionJob>(sql, new
            {
                Status = status.ToString(),
                Limit = limit
            });
            return jobs.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get ingestion jobs by status {Status}", status);
            throw;
        }
    }

    /// <summary>
    /// Gets ingestion jobs by job type
    /// </summary>
    public async Task<IngestionJob[]> GetByJobTypeAsync(IngestionJobType jobType, int limit = 100)
    {
        if (limit <= 0)
        {
            throw new ArgumentException("Limit must be greater than 0", nameof(limit));
        }

        const string sql = @"
            EXEC [dbo].[sp_GetIngestionJobsByJobType]
                @JobType = @JobType,
                @Limit = @Limit;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var jobs = await connection.QueryAsync<IngestionJob>(sql, new
            {
                JobType = jobType.ToString(),
                Limit = limit
            });
            return jobs.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get ingestion jobs by type {JobType}", jobType);
            throw;
        }
    }

    /// <summary>
    /// Gets recent ingestion jobs
    /// </summary>
    public async Task<IngestionJob[]> GetRecentJobsAsync(int limit = 50)
    {
        if (limit <= 0)
        {
            throw new ArgumentException("Limit must be greater than 0", nameof(limit));
        }

        const string sql = @"
            EXEC [dbo].[sp_GetRecentIngestionJobs] @Limit = @Limit;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var jobs = await connection.QueryAsync<IngestionJob>(sql, new { Limit = limit });
            return jobs.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get recent ingestion jobs with limit {Limit}", limit);
            throw;
        }
    }

    /// <summary>
    /// Gets ingestion jobs for a specific user
    /// </summary>
    public async Task<IngestionJob[]> GetByUserIdAsync(string userId, int limit = 100)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User ID cannot be null or empty", nameof(userId));
        }

        if (limit <= 0)
        {
            throw new ArgumentException("Limit must be greater than 0", nameof(limit));
        }

        const string sql = @"
            EXEC [dbo].[sp_GetIngestionJobsByUser]
                @UserId = @UserId,
                @Limit = @Limit;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var jobs = await connection.QueryAsync<IngestionJob>(sql, new
            {
                UserId = userId,
                Limit = limit
            });
            return jobs.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get ingestion jobs for user {UserId}", SanitizeForLog(userId));
            throw;
        }
    }

    /// <summary>
    /// Gets ingestion jobs within a date range
    /// </summary>
    public async Task<IngestionJob[]> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        const string sql = @"
            EXEC [dbo].[sp_GetIngestionJobsByDateRange]
                @StartDate = @StartDate,
                @EndDate = @EndDate;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var jobs = await connection.QueryAsync<IngestionJob>(sql, new
            {
                StartDate = startDate,
                EndDate = endDate
            });
            return jobs.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get ingestion jobs between {StartDate} and {EndDate}", startDate, endDate);
            throw;
        }
    }

    /// <summary>
    /// Deletes an ingestion job by its ID
    /// </summary>
    public async Task<bool> DeleteAsync(long id)
    {
        if (id <= 0)
        {
            throw new ArgumentException("ID must be greater than 0", nameof(id));
        }

        const string sql = @"
            DELETE FROM [dbo].[IngestionJobs]
            WHERE [Id] = @Id;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var rowsAffected = await connection.ExecuteAsync(sql, new { Id = id });
            
            if (rowsAffected > 0)
            {
                _logger.LogDebug("Deleted ingestion job with ID {JobId}", id);
            }
            
            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete ingestion job with ID {JobId}", id);
            throw;
        }
    }

    /// <summary>
    /// Sanitizes a string for logging by removing newlines and tabs
    /// </summary>
    private string SanitizeForLog(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        return input.Replace("\n", " ").Replace("\r", " ").Replace("\t", " ");
    }
}
