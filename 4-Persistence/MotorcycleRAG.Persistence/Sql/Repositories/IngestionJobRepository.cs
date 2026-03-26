using Dapper;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;
using MotorcycleRAG.Persistence.Sql;

namespace MotorcycleRAG.Persistence.Sql.Repositories;

/// <summary>
/// Dapper-based implementation of <see cref="IIngestionJobRepository"/>.
/// All SQL uses parameterized queries — no string concatenation.
/// </summary>
public class IngestionJobRepository : IIngestionJobRepository
{
    private readonly ISqlConnectionFactory _connectionFactory;
    private readonly ILogger<IngestionJobRepository> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="IngestionJobRepository"/>.
    /// </summary>
    /// <param name="connectionFactory">SQL connection factory.</param>
    /// <param name="logger">Logger instance.</param>
    public IngestionJobRepository(ISqlConnectionFactory connectionFactory, ILogger<IngestionJobRepository> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<IngestionJob> CreateAsync(IngestionJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        const string sql = @"
            INSERT INTO [dbo].[IngestionJobs] (
                [IngestionJobId], [CreatedAtUtc], [StartedAtUtc], [CompletedAtUtc],
                [CreatedBySubject], [Status], [FailureReason], [InputType], [InputRef],
                [ComputeProvider], [FabricRunId], [ManualDocumentId],
                [TotalPages], [PagesCapturedViewableCount], [PagesWithSearchableTextCount],
                [PagesWithOcrTextCount], [PagesWithNativeTextCount],
                [MissingPagesJson], [MetricsJson]
            )
            VALUES (
                @IngestionJobId, @CreatedAtUtc, @StartedAtUtc, @CompletedAtUtc,
                @CreatedBySubject, @Status, @FailureReason, @InputType, @InputRef,
                @ComputeProvider, @FabricRunId, @ManualDocumentId,
                @TotalPages, @PagesCapturedViewableCount, @PagesWithSearchableTextCount,
                @PagesWithOcrTextCount, @PagesWithNativeTextCount,
                @MissingPagesJson, @MetricsJson
            );
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            await connection.ExecuteAsync(new CommandDefinition(sql, new
            {
                job.IngestionJobId,
                CreatedAtUtc = job.CreatedAtUtc.UtcDateTime,
                StartedAtUtc = job.StartedAtUtc?.UtcDateTime,
                CompletedAtUtc = job.CompletedAtUtc?.UtcDateTime,
                job.CreatedBySubject,
                Status = (int)job.Status,
                job.FailureReason,
                InputType = (int)job.InputType,
                job.InputRef,
                job.ComputeProvider,
                job.FabricRunId,
                job.ManualDocumentId,
                job.TotalPages,
                job.PagesCapturedViewableCount,
                job.PagesWithSearchableTextCount,
                job.PagesWithOcrTextCount,
                job.PagesWithNativeTextCount,
                job.MissingPagesJson,
                job.MetricsJson
            }, cancellationToken: cancellationToken));

            _logger.LogInformation("Created ingestion job {IngestionJobId}", job.IngestionJobId);
            return job;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create ingestion job {IngestionJobId}", job.IngestionJobId);
            throw new InvalidOperationException($"Failed to create ingestion job {job.IngestionJobId}", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<IngestionJob?> GetByIdAsync(Guid ingestionJobId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT [IngestionJobId], [CreatedAtUtc], [StartedAtUtc], [CompletedAtUtc],
                   [CreatedBySubject], [Status], [FailureReason], [InputType], [InputRef],
                   [ComputeProvider], [FabricRunId], [ManualDocumentId],
                   [TotalPages], [PagesCapturedViewableCount], [PagesWithSearchableTextCount],
                   [PagesWithOcrTextCount], [PagesWithNativeTextCount],
                   [MissingPagesJson], [MetricsJson]
            FROM [dbo].[IngestionJobs]
            WHERE [IngestionJobId] = @IngestionJobId;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            return await connection.QueryFirstOrDefaultAsync<IngestionJob>(
                new CommandDefinition(sql, new { IngestionJobId = ingestionJobId }, cancellationToken: cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get ingestion job {IngestionJobId}", ingestionJobId);
            throw new InvalidOperationException($"Failed to get ingestion job {ingestionJobId}", ex);
        }
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(IngestionJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        const string sql = @"
            UPDATE [dbo].[IngestionJobs] SET
                [StartedAtUtc] = @StartedAtUtc,
                [CompletedAtUtc] = @CompletedAtUtc,
                [CreatedBySubject] = @CreatedBySubject,
                [Status] = @Status,
                [FailureReason] = @FailureReason,
                [InputType] = @InputType,
                [InputRef] = @InputRef,
                [ComputeProvider] = @ComputeProvider,
                [FabricRunId] = @FabricRunId,
                [ManualDocumentId] = @ManualDocumentId,
                [TotalPages] = @TotalPages,
                [PagesCapturedViewableCount] = @PagesCapturedViewableCount,
                [PagesWithSearchableTextCount] = @PagesWithSearchableTextCount,
                [PagesWithOcrTextCount] = @PagesWithOcrTextCount,
                [PagesWithNativeTextCount] = @PagesWithNativeTextCount,
                [MissingPagesJson] = @MissingPagesJson,
                [MetricsJson] = @MetricsJson
            WHERE [IngestionJobId] = @IngestionJobId;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            await connection.ExecuteAsync(new CommandDefinition(sql, new
            {
                job.IngestionJobId,
                StartedAtUtc = job.StartedAtUtc?.UtcDateTime,
                CompletedAtUtc = job.CompletedAtUtc?.UtcDateTime,
                job.CreatedBySubject,
                Status = (int)job.Status,
                job.FailureReason,
                InputType = (int)job.InputType,
                job.InputRef,
                job.ComputeProvider,
                job.FabricRunId,
                job.ManualDocumentId,
                job.TotalPages,
                job.PagesCapturedViewableCount,
                job.PagesWithSearchableTextCount,
                job.PagesWithOcrTextCount,
                job.PagesWithNativeTextCount,
                job.MissingPagesJson,
                job.MetricsJson
            }, cancellationToken: cancellationToken));

            _logger.LogInformation("Updated ingestion job {IngestionJobId}", job.IngestionJobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update ingestion job {IngestionJobId}", job.IngestionJobId);
            throw new InvalidOperationException($"Failed to update ingestion job {job.IngestionJobId}", ex);
        }
    }

    /// <inheritdoc/>
    public async Task UpdateStatusAsync(
        Guid ingestionJobId,
        IngestionJobStatus status,
        string? failureReason = null,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE [dbo].[IngestionJobs] SET
                [Status] = @Status,
                [FailureReason] = @FailureReason
            WHERE [IngestionJobId] = @IngestionJobId;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            await connection.ExecuteAsync(new CommandDefinition(sql, new
            {
                IngestionJobId = ingestionJobId,
                Status = (int)status,
                FailureReason = failureReason
            }, cancellationToken: cancellationToken));

            _logger.LogInformation("Updated ingestion job {IngestionJobId} status to {Status}",
                ingestionJobId, status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update status for ingestion job {IngestionJobId}", ingestionJobId);
            throw new InvalidOperationException($"Failed to update status for ingestion job {ingestionJobId}", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<IngestionJob?> GetLatestByInputRefAsync(string inputRef, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputRef);

        const string sql = @"
            SELECT TOP 1
                [IngestionJobId], [CreatedAtUtc], [StartedAtUtc], [CompletedAtUtc],
                [CreatedBySubject], [Status], [FailureReason], [InputType], [InputRef],
                [ComputeProvider], [FabricRunId], [ManualDocumentId],
                [TotalPages], [PagesCapturedViewableCount], [PagesWithSearchableTextCount],
                [PagesWithOcrTextCount], [PagesWithNativeTextCount],
                [MissingPagesJson], [MetricsJson]
            FROM [dbo].[IngestionJobs]
            WHERE [InputRef] = @InputRef
            ORDER BY [CreatedAtUtc] DESC;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            return await connection.QueryFirstOrDefaultAsync<IngestionJob>(
                new CommandDefinition(sql, new { InputRef = inputRef }, cancellationToken: cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get latest ingestion job by input ref");
            throw new InvalidOperationException("Failed to get latest ingestion job by input ref", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<IngestionJob?> GetLatestByInputAsync(
        string inputRef,
        IngestionJobType inputType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputRef);

        const string sql = @"
            SELECT TOP 1
                [IngestionJobId], [CreatedAtUtc], [StartedAtUtc], [CompletedAtUtc],
                [CreatedBySubject], [Status], [FailureReason], [InputType], [InputRef],
                [ComputeProvider], [FabricRunId], [ManualDocumentId],
                [TotalPages], [PagesCapturedViewableCount], [PagesWithSearchableTextCount],
                [PagesWithOcrTextCount], [PagesWithNativeTextCount],
                [MissingPagesJson], [MetricsJson]
            FROM [dbo].[IngestionJobs]
            WHERE [InputRef] = @InputRef
              AND [InputType] = @InputType
            ORDER BY [CreatedAtUtc] DESC;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            return await connection.QueryFirstOrDefaultAsync<IngestionJob>(
                new CommandDefinition(sql, new { InputRef = inputRef, InputType = (int)inputType }, cancellationToken: cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get latest ingestion job by input ref {InputRef} and type {InputType}", inputRef, inputType);
            throw new InvalidOperationException("Failed to get latest ingestion job by input ref and type", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<IngestionJob>> GetRecentAsync(
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        if (maxCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxCount), "maxCount must be greater than zero.");

        const string sql = @"
            SELECT TOP (@MaxCount)
                [IngestionJobId], [CreatedAtUtc], [StartedAtUtc], [CompletedAtUtc],
                [CreatedBySubject], [Status], [FailureReason], [InputType], [InputRef],
                [ComputeProvider], [FabricRunId], [ManualDocumentId],
                [TotalPages], [PagesCapturedViewableCount], [PagesWithSearchableTextCount],
                [PagesWithOcrTextCount], [PagesWithNativeTextCount],
                [MissingPagesJson], [MetricsJson]
            FROM [dbo].[IngestionJobs]
            ORDER BY [CreatedAtUtc] DESC;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var results = await connection.QueryAsync<IngestionJob>(
                new CommandDefinition(sql, new { MaxCount = maxCount }, cancellationToken: cancellationToken));
            return results.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get recent ingestion jobs");
            throw new InvalidOperationException("Failed to get recent ingestion jobs", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<IngestionJob>> GetByManualDocumentIdAsync(
        Guid manualDocumentId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT [IngestionJobId], [CreatedAtUtc], [StartedAtUtc], [CompletedAtUtc],
                   [CreatedBySubject], [Status], [FailureReason], [InputType], [InputRef],
                   [ComputeProvider], [FabricRunId], [ManualDocumentId],
                   [TotalPages], [PagesCapturedViewableCount], [PagesWithSearchableTextCount],
                   [PagesWithOcrTextCount], [PagesWithNativeTextCount],
                   [MissingPagesJson], [MetricsJson]
            FROM [dbo].[IngestionJobs]
            WHERE [ManualDocumentId] = @ManualDocumentId
            ORDER BY [CreatedAtUtc] DESC;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var results = await connection.QueryAsync<IngestionJob>(
                new CommandDefinition(sql, new { ManualDocumentId = manualDocumentId }, cancellationToken: cancellationToken));

            _logger.LogInformation("Retrieved {Count} ingestion jobs for manual document {ManualDocumentId}",
                results.AsList().Count, manualDocumentId);

            return results.ToList().AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get ingestion jobs for manual document {ManualDocumentId}", manualDocumentId);
            throw new InvalidOperationException($"Failed to get ingestion jobs for manual document {manualDocumentId}", ex);
        }
    }
}
