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
                [CreatedBySubject], [Status], [FailureReason], [ErrorsJson], [ErrorMessage], [InputType], [InputRef],
                [ComputeProvider], [DocIngestionRunId], [ManualDocumentId],
                [TotalPages], [PagesCapturedViewableCount], [PagesWithSearchableTextCount],
                [PagesWithOcrTextCount], [PagesWithNativeTextCount],
                [MissingPagesJson], [MetricsJson], [ExpectedChunkCount], [IndexedChunkCount]
            )
            VALUES (
                @IngestionJobId, @CreatedAtUtc, @StartedAtUtc, @CompletedAtUtc,
                @CreatedBySubject, @Status, @FailureReason, @ErrorsJson, @ErrorMessage, @InputType, @InputRef,
                @ComputeProvider, @DocIngestionRunId, @ManualDocumentId,
                @TotalPages, @PagesCapturedViewableCount, @PagesWithSearchableTextCount,
                @PagesWithOcrTextCount, @PagesWithNativeTextCount,
                @MissingPagesJson, @MetricsJson, @ExpectedChunkCount, @IndexedChunkCount
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
                Status = job.Status.ToString(),
                job.FailureReason,
                job.ErrorsJson,
                job.ErrorMessage,
                InputType = job.InputType.ToString(),
                job.InputRef,
                job.ComputeProvider,
                job.DocIngestionRunId,
                job.ManualDocumentId,
                job.TotalPages,
                job.PagesCapturedViewableCount,
                job.PagesWithSearchableTextCount,
                job.PagesWithOcrTextCount,
                job.PagesWithNativeTextCount,
                job.MissingPagesJson,
                job.MetricsJson,
                job.ExpectedChunkCount,
                job.IndexedChunkCount
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
                   [CreatedBySubject], [Status], [FailureReason], [ErrorsJson], [ErrorMessage], [InputType], [InputRef],
                   [ComputeProvider], [DocIngestionRunId], [ManualDocumentId],
                   [TotalPages], [PagesCapturedViewableCount], [PagesWithSearchableTextCount],
                   [PagesWithOcrTextCount], [PagesWithNativeTextCount],
                   [MissingPagesJson], [MetricsJson], [ExpectedChunkCount], [IndexedChunkCount]
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
                [ErrorsJson] = @ErrorsJson,
                [ErrorMessage] = @ErrorMessage,
                [InputType] = @InputType,
                [InputRef] = @InputRef,
                [ComputeProvider] = @ComputeProvider,
                [DocIngestionRunId] = @DocIngestionRunId,
                [ManualDocumentId] = @ManualDocumentId,
                [TotalPages] = @TotalPages,
                [PagesCapturedViewableCount] = @PagesCapturedViewableCount,
                [PagesWithSearchableTextCount] = @PagesWithSearchableTextCount,
                [PagesWithOcrTextCount] = @PagesWithOcrTextCount,
                [PagesWithNativeTextCount] = @PagesWithNativeTextCount,
                [MissingPagesJson] = @MissingPagesJson,
                [MetricsJson] = @MetricsJson,
                [ExpectedChunkCount] = @ExpectedChunkCount,
                [IndexedChunkCount] = @IndexedChunkCount
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
                Status = job.Status.ToString(),
                job.FailureReason,
                job.ErrorsJson,
                job.ErrorMessage,
                InputType = job.InputType.ToString(),
                job.InputRef,
                job.ComputeProvider,
                job.DocIngestionRunId,
                job.ManualDocumentId,
                job.TotalPages,
                job.PagesCapturedViewableCount,
                job.PagesWithSearchableTextCount,
                job.PagesWithOcrTextCount,
                job.PagesWithNativeTextCount,
                job.MissingPagesJson,
                job.MetricsJson,
                job.ExpectedChunkCount,
                job.IndexedChunkCount
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
                Status = status.ToString(),
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
                [CreatedBySubject], [Status], [FailureReason], [ErrorsJson], [ErrorMessage], [InputType], [InputRef],
                [ComputeProvider], [DocIngestionRunId], [ManualDocumentId],
                [TotalPages], [PagesCapturedViewableCount], [PagesWithSearchableTextCount],
                [PagesWithOcrTextCount], [PagesWithNativeTextCount],
                [MissingPagesJson], [MetricsJson], [ExpectedChunkCount], [IndexedChunkCount]
            FROM [dbo].[IngestionJobs]
            WHERE [InputRef] = @InputRef
            ORDER BY COALESCE([CreatedAtUtc], [CreatedAt], [StartTime]) DESC;
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
                [CreatedBySubject], [Status], [FailureReason], [ErrorsJson], [ErrorMessage], [InputType], [InputRef],
                [ComputeProvider], [DocIngestionRunId], [ManualDocumentId],
                [TotalPages], [PagesCapturedViewableCount], [PagesWithSearchableTextCount],
                [PagesWithOcrTextCount], [PagesWithNativeTextCount],
                [MissingPagesJson], [MetricsJson], [ExpectedChunkCount], [IndexedChunkCount]
            FROM [dbo].[IngestionJobs]
            WHERE [InputRef] = @InputRef
              AND [InputType] = @InputType
            ORDER BY COALESCE([CreatedAtUtc], [CreatedAt], [StartTime]) DESC;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            return await connection.QueryFirstOrDefaultAsync<IngestionJob>(
                new CommandDefinition(sql, new { InputRef = inputRef, InputType = inputType.ToString() }, cancellationToken: cancellationToken));
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
                [CreatedBySubject], [Status], [FailureReason], [ErrorsJson], [ErrorMessage], [InputType], [InputRef],
                [ComputeProvider], [DocIngestionRunId], [ManualDocumentId],
                [TotalPages], [PagesCapturedViewableCount], [PagesWithSearchableTextCount],
                [PagesWithOcrTextCount], [PagesWithNativeTextCount],
                [MissingPagesJson], [MetricsJson], [ExpectedChunkCount], [IndexedChunkCount]
            FROM [dbo].[IngestionJobs]
            ORDER BY COALESCE([CreatedAtUtc], [CreatedAt], [StartTime]) DESC;
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
                   [CreatedBySubject], [Status], [FailureReason], [ErrorsJson], [ErrorMessage], [InputType], [InputRef],
                   [ComputeProvider], [DocIngestionRunId], [ManualDocumentId],
                   [TotalPages], [PagesCapturedViewableCount], [PagesWithSearchableTextCount],
                   [PagesWithOcrTextCount], [PagesWithNativeTextCount],
                   [MissingPagesJson], [MetricsJson], [ExpectedChunkCount], [IndexedChunkCount]
            FROM [dbo].[IngestionJobs]
            WHERE [ManualDocumentId] = @ManualDocumentId
            ORDER BY COALESCE([CreatedAtUtc], [CreatedAt], [StartTime]) DESC;
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

    /// <inheritdoc/>
    public async Task<IReadOnlyList<IngestionJob>> GetByStatusesAsync(
        IReadOnlyCollection<IngestionJobStatus> statuses,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        if (statuses.Count == 0)
        {
            return Array.Empty<IngestionJob>();
        }

        const string sql = @"
            SELECT [IngestionJobId], [CreatedAtUtc], [StartedAtUtc], [CompletedAtUtc],
                   [CreatedBySubject], [Status], [FailureReason], [ErrorsJson], [ErrorMessage], [InputType], [InputRef],
                   [ComputeProvider], [DocIngestionRunId], [ManualDocumentId],
                   [TotalPages], [PagesCapturedViewableCount], [PagesWithSearchableTextCount],
                   [PagesWithOcrTextCount], [PagesWithNativeTextCount],
                   [MissingPagesJson], [MetricsJson], [ExpectedChunkCount], [IndexedChunkCount]
            FROM [dbo].[IngestionJobs]
            WHERE [Status] IN @Statuses
            ORDER BY COALESCE([CreatedAtUtc], [CreatedAt], [StartTime]) DESC;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var results = await connection.QueryAsync<IngestionJob>(
                new CommandDefinition(
                    sql,
                    new { Statuses = statuses.Select(static status => status.ToString()).ToArray() },
                    cancellationToken: cancellationToken));

            return results.ToList().AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get ingestion jobs by status");
            throw new InvalidOperationException("Failed to get ingestion jobs by status", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(
        Guid ingestionJobId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            DELETE FROM [dbo].[IngestionJobs]
            WHERE [IngestionJobId] = @IngestionJobId;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var affectedRows = await connection.ExecuteAsync(
                new CommandDefinition(sql, new { IngestionJobId = ingestionJobId }, cancellationToken: cancellationToken));

            return affectedRows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete ingestion job {IngestionJobId}", ingestionJobId);
            throw new InvalidOperationException($"Failed to delete ingestion job {ingestionJobId}", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<int> DeleteByInputRefAsync(
        string inputRef,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputRef);

        const string sql = @"
            DELETE FROM [dbo].[IngestionJobs]
            WHERE [InputRef] = @InputRef;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            return await connection.ExecuteAsync(
                new CommandDefinition(sql, new { InputRef = inputRef }, cancellationToken: cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete ingestion jobs for input ref {InputRef}", inputRef);
            throw new InvalidOperationException("Failed to delete ingestion jobs by input ref", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<int> DeleteByStatusesAsync(
        IReadOnlyCollection<IngestionJobStatus> statuses,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        if (statuses.Count == 0)
        {
            return 0;
        }

        const string sql = @"
            DELETE FROM [dbo].[IngestionJobs]
            WHERE [Status] IN @Statuses;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            return await connection.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    new { Statuses = statuses.Select(static status => status.ToString()).ToArray() },
                    cancellationToken: cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete ingestion jobs by status");
            throw new InvalidOperationException("Failed to delete ingestion jobs by status", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<int> DeleteByIdsAsync(
        IReadOnlyCollection<Guid> ingestionJobIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ingestionJobIds);
        if (ingestionJobIds.Count == 0)
        {
            return 0;
        }

        const string sql = @"
            DELETE FROM [dbo].[IngestionJobs]
            WHERE [IngestionJobId] IN @IngestionJobIds;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            return await connection.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    new { IngestionJobIds = ingestionJobIds.ToArray() },
                    cancellationToken: cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete ingestion jobs by identifier batch");
            throw new InvalidOperationException("Failed to delete ingestion jobs by identifier batch", ex);
        }
    }

    /// <summary>
    /// Atomically transitions a job to a terminal status only if it currently matches fromStatus.
    /// Returns true if the update succeeded (WHERE clause matched), false otherwise (replay-safe).
    /// </summary>
    public async Task<bool> TryTransitionToTerminalAsync(
        Guid jobId,
        IngestionJobStatus fromStatus,
        IngestionJobStatus toStatus,
        int? expectedChunkCount,
        int? indexedChunkCount,
        string? failureReason,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE [dbo].[IngestionJobs] SET
                [Status] = @ToStatus,
                [ExpectedChunkCount] = @ExpectedChunkCount,
                [IndexedChunkCount] = @IndexedChunkCount,
                [CompletedAtUtc] = SYSUTCDATETIME(),
                [FailureReason] = @FailureReason
            WHERE [IngestionJobId] = @IngestionJobId
              AND [Status] = @FromStatus;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var affectedRows = await connection.ExecuteAsync(new CommandDefinition(sql, new
            {
                IngestionJobId = jobId,
                FromStatus = fromStatus.ToString(),
                ToStatus = toStatus.ToString(),
                ExpectedChunkCount = expectedChunkCount,
                IndexedChunkCount = indexedChunkCount,
                FailureReason = failureReason
            }, cancellationToken: cancellationToken));

            var success = affectedRows > 0;
            if (success)
            {
                _logger.LogInformation(
                    "Successfully transitioned ingestion job {IngestionJobId} from {FromStatus} to {ToStatus}",
                    jobId, fromStatus, toStatus);
            }
            else
            {
                _logger.LogInformation(
                    "Failed to transition ingestion job {IngestionJobId}: status mismatch (expected {FromStatus})",
                    jobId, fromStatus);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to transition ingestion job {IngestionJobId} to terminal status", jobId);
            throw new InvalidOperationException($"Failed to transition ingestion job {jobId} to terminal status", ex);
        }
    }

    /// <inheritdoc/>
    public async Task UpdateStageAsync(
        Guid ingestionJobId,
        string stage,
        int? chunksProcessed,
        int? totalChunks,
        string? failureReason,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE [dbo].[IngestionJobs] SET
                [CurrentStage] = @Stage,
                [StageSetAtUtc] = SYSUTCDATETIME(),
                [ExpectedChunkCount] = COALESCE(@TotalChunks, [ExpectedChunkCount]),
                [IndexedChunkCount] = COALESCE(@ChunksProcessed, [IndexedChunkCount]),
                [FailureReason] = COALESCE(@FailureReason, [FailureReason])
            WHERE [IngestionJobId] = @IngestionJobId;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            await connection.ExecuteAsync(new CommandDefinition(sql, new
            {
                IngestionJobId = ingestionJobId,
                Stage = stage,
                TotalChunks = totalChunks,
                ChunksProcessed = chunksProcessed,
                FailureReason = failureReason
            }, cancellationToken: cancellationToken));

            _logger.LogInformation(
                "Updated stage for ingestion job {IngestionJobId} to {Stage} (chunks={ChunksProcessed}/{TotalChunks})",
                ingestionJobId, stage, chunksProcessed, totalChunks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update stage for ingestion job {IngestionJobId}", ingestionJobId);
            throw new InvalidOperationException($"Failed to update stage for ingestion job {ingestionJobId}", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<IngestionJob?> GetByDocIngestionRunIdAsync(
        string docIngestionRunId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(docIngestionRunId);

        const string sql = @"
            SELECT [IngestionJobId], [CreatedAtUtc], [StartedAtUtc], [CompletedAtUtc],
                   [CreatedBySubject], [Status], [FailureReason], [ErrorsJson], [ErrorMessage], [InputType], [InputRef],
                   [ComputeProvider], [DocIngestionRunId], [ManualDocumentId],
                   [TotalPages], [PagesCapturedViewableCount], [PagesWithSearchableTextCount],
                   [PagesWithOcrTextCount], [PagesWithNativeTextCount],
                   [MissingPagesJson], [MetricsJson], [ExpectedChunkCount], [IndexedChunkCount],
                   [CurrentStage], [StageSetAtUtc]
            FROM [dbo].[IngestionJobs]
            WHERE [DocIngestionRunId] = @DocIngestionRunId;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            return await connection.QueryFirstOrDefaultAsync<IngestionJob>(
                new CommandDefinition(sql, new { DocIngestionRunId = docIngestionRunId }, cancellationToken: cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get ingestion job by doc ingestion run id {DocIngestionRunId}", docIngestionRunId);
            throw new InvalidOperationException($"Failed to get ingestion job by doc ingestion run id {docIngestionRunId}", ex);
        }
    }
}

