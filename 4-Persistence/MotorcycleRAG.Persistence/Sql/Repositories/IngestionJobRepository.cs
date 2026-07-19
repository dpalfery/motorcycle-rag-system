using System.Data;
using System.Diagnostics;
using Dapper;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;
using MotorcycleRAG.Persistence.Sql;

namespace MotorcycleRAG.Persistence.Sql.Repositories;

/// <summary>
/// Dapper-based implementation of <see cref="IIngestionJobRepository"/>.
/// Query values are parameterized; schema-dependent SQL fragments are trusted constants.
/// </summary>
public class IngestionJobRepository : IIngestionJobRepository
{
    /// <summary>
    /// Activity source used to emit distributed tracing spans for SQL dependency tracking.
    /// The legacy Application Insights SDK consumes <see cref="Activity.Current"/>; this source
    /// is also forward-compatible with OpenTelemetry listeners if one is added later.
    /// </summary>
    private static readonly ActivitySource ActivitySource = new("MotorcycleRAG.IngestionJobRepository");

    private readonly ISqlConnectionFactory _connectionFactory;
    private readonly ILogger<IngestionJobRepository> _logger;

    /// <summary>
    /// Process-lifetime cache for whether <c>dbo.IngestionJobs.Id</c> exists.
    /// Tri-state: -1 = unknown (query pending), 0 = false, 1 = true.
    /// Schema column presence is a deploy-time invariant, so a single metadata round-trip
    /// per application lifetime is sufficient regardless of concurrency.
    /// </summary>
    private static volatile int _hasSqlIdColumn = -1;

    /// <summary>
    /// Clears the cached <c>dbo.IngestionJobs.Id</c> capability so the next operation probes
    /// the schema again. Intended for controlled schema changes and deterministic tests.
    /// </summary>
    internal static void InvalidateSchemaMetadataCache()
    {
        Interlocked.Exchange(ref _hasSqlIdColumn, -1);
    }

    private const string IngestionJobColumnsWithSqlId = @"
                [Id], [IngestionJobId], [CreatedAtUtc], [StartedAtUtc], [CompletedAtUtc],
                [CreatedBySubject], [Status], [FailureReason], [ErrorsJson], [ErrorMessage], [InputType], [InputRef], [SourceFileName],
                [ComputeProvider], [DocIngestionRunId], [ManualDocumentId],
                [TotalPages], [PagesCapturedViewableCount], [PagesWithSearchableTextCount],
                [PagesWithOcrTextCount], [PagesWithNativeTextCount],
                [MissingPagesJson], [MetricsJson], [ExpectedChunkCount], [IndexedChunkCount],
                [CurrentStage], [StageSetAtUtc], [MetadataJson]";

    private const string IngestionJobColumnsWithoutSqlId = @"
                CAST(0 AS BIGINT) AS [Id], [IngestionJobId], [CreatedAtUtc], [StartedAtUtc], [CompletedAtUtc],
                [CreatedBySubject], [Status], [FailureReason], [ErrorsJson], [ErrorMessage], [InputType], [InputRef], [SourceFileName],
                [ComputeProvider], [DocIngestionRunId], [ManualDocumentId],
                [TotalPages], [PagesCapturedViewableCount], [PagesWithSearchableTextCount],
                [PagesWithOcrTextCount], [PagesWithNativeTextCount],
                [MissingPagesJson], [MetricsJson], [ExpectedChunkCount], [IndexedChunkCount],
                [CurrentStage], [StageSetAtUtc], [MetadataJson]";

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

    private static async Task<bool> HasSqlIdColumnAsync(
        IDbConnection connection,
        CancellationToken cancellationToken)
    {
        // Fast path: cache already populated by a previous call (on any thread).
        var cached = _hasSqlIdColumn;
        if (cached >= 0)
        {
            return cached == 1;
        }

        // Cold path: execute the metadata probe at most once per process lifetime.
        // The schema column presence never changes at runtime, so concurrent callers may
        // race to execute the query, but only the first writes via CompareExchange; the
        // rest observe the settled value and reuse it forever after.
        const string sql = "SELECT CASE WHEN COL_LENGTH('dbo.IngestionJobs', 'Id') IS NULL THEN 0 ELSE 1 END;";
        var result = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));

        Interlocked.CompareExchange(ref _hasSqlIdColumn, result, -1);
        return _hasSqlIdColumn == 1;
    }

    private static string GetIngestionJobColumns(bool hasSqlIdColumn) =>
        hasSqlIdColumn ? IngestionJobColumnsWithSqlId : IngestionJobColumnsWithoutSqlId;

    /// <inheritdoc/>
    public async Task<IngestionJob> CreateAsync(IngestionJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        const string insertSqlWithSqlId = @"
            INSERT INTO [dbo].[IngestionJobs] (
                [IngestionJobId], [CreatedAtUtc], [StartedAtUtc], [CompletedAtUtc],
                [CreatedBySubject], [Status], [FailureReason], [ErrorsJson], [ErrorMessage], [InputType], [InputRef], [SourceFileName],
                [ComputeProvider], [DocIngestionRunId], [ManualDocumentId],
                [TotalPages], [PagesCapturedViewableCount], [PagesWithSearchableTextCount],
                [PagesWithOcrTextCount], [PagesWithNativeTextCount],
                [MissingPagesJson], [MetricsJson], [ExpectedChunkCount], [IndexedChunkCount],
                [CurrentStage], [StageSetAtUtc]
            )
            OUTPUT INSERTED.[Id]
            VALUES (
                @IngestionJobId, @CreatedAtUtc, @StartedAtUtc, @CompletedAtUtc,
                @CreatedBySubject, @Status, @FailureReason, @ErrorsJson, @ErrorMessage, @InputType, @InputRef, @SourceFileName,
                @ComputeProvider, @DocIngestionRunId, @ManualDocumentId,
                @TotalPages, @PagesCapturedViewableCount, @PagesWithSearchableTextCount,
                @PagesWithOcrTextCount, @PagesWithNativeTextCount,
                @MissingPagesJson, @MetricsJson, @ExpectedChunkCount, @IndexedChunkCount,
                @CurrentStage, @StageSetAtUtc
            );
        ";

        const string insertSqlWithoutSqlId = @"
            INSERT INTO [dbo].[IngestionJobs] (
                [IngestionJobId], [CreatedAtUtc], [StartedAtUtc], [CompletedAtUtc],
                [CreatedBySubject], [Status], [FailureReason], [ErrorsJson], [ErrorMessage], [InputType], [InputRef], [SourceFileName],
                [ComputeProvider], [DocIngestionRunId], [ManualDocumentId],
                [TotalPages], [PagesCapturedViewableCount], [PagesWithSearchableTextCount],
                [PagesWithOcrTextCount], [PagesWithNativeTextCount],
                [MissingPagesJson], [MetricsJson], [ExpectedChunkCount], [IndexedChunkCount],
                [CurrentStage], [StageSetAtUtc]
            )
            VALUES (
                @IngestionJobId, @CreatedAtUtc, @StartedAtUtc, @CompletedAtUtc,
                @CreatedBySubject, @Status, @FailureReason, @ErrorsJson, @ErrorMessage, @InputType, @InputRef, @SourceFileName,
                @ComputeProvider, @DocIngestionRunId, @ManualDocumentId,
                @TotalPages, @PagesCapturedViewableCount, @PagesWithSearchableTextCount,
                @PagesWithOcrTextCount, @PagesWithNativeTextCount,
                @MissingPagesJson, @MetricsJson, @ExpectedChunkCount, @IndexedChunkCount,
                @CurrentStage, @StageSetAtUtc
            );
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var parameters = new
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
                job.SourceFileName,
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
                job.IndexedChunkCount,
                job.CurrentStage,
                StageSetAtUtc = job.StageSetAtUtc?.UtcDateTime
            };

            long assignedId;
            if (await HasSqlIdColumnAsync(connection, cancellationToken))
            {
                assignedId = await connection.QuerySingleAsync<long>(
                    new CommandDefinition(insertSqlWithSqlId, parameters, cancellationToken: cancellationToken));
            }
            else
            {
                await connection.ExecuteAsync(
                    new CommandDefinition(insertSqlWithoutSqlId, parameters, cancellationToken: cancellationToken));
                assignedId = 0;
            }

            // The entity's identity is immutable, so we cannot mutate job.Id. Return a fresh
            // rehydrated instance carrying the assigned database identity for callers to use.
            var persisted = IngestionJob.Rehydrate(
                assignedId,
                job.IngestionJobId,
                job.CreatedAtUtc,
                job.StartedAtUtc,
                job.CompletedAtUtc,
                job.CreatedBySubject,
                job.Status,
                job.FailureReason,
                job.ErrorsJson,
                job.ErrorMessage,
                job.InputType,
                job.InputRef,
                job.SourceFileName,
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
                job.IndexedChunkCount,
                job.CurrentStage,
                job.StageSetAtUtc,
                job.MetadataJson);

            _logger.LogInformation("Created ingestion job {IngestionJobId}", persisted.IngestionJobId);
            return persisted;
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
        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var sql = $@"
            SELECT {GetIngestionJobColumns(await HasSqlIdColumnAsync(connection, cancellationToken))}
            FROM [dbo].[IngestionJobs]
            WHERE [IngestionJobId] = @IngestionJobId;
        ";
            var row = await connection.QueryFirstOrDefaultAsync<IngestionJobRow>(
                new CommandDefinition(sql, new { IngestionJobId = ingestionJobId }, cancellationToken: cancellationToken));
            return row is null ? null : Map(row);
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
                [SourceFileName] = @SourceFileName,
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
                [IndexedChunkCount] = @IndexedChunkCount,
                [CurrentStage] = @CurrentStage,
                [StageSetAtUtc] = @StageSetAtUtc
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
                job.SourceFileName,
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
                job.IndexedChunkCount,
                job.CurrentStage,
                StageSetAtUtc = job.StageSetAtUtc?.UtcDateTime
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

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var sql = $@"
            SELECT TOP 1 {GetIngestionJobColumns(await HasSqlIdColumnAsync(connection, cancellationToken))}
            FROM [dbo].[IngestionJobs]
            WHERE [InputRef] = @InputRef
            ORDER BY [CreatedAtUtc] DESC;
        ";
            var row = await connection.QueryFirstOrDefaultAsync<IngestionJobRow>(
                new CommandDefinition(sql, new { InputRef = inputRef }, cancellationToken: cancellationToken));
            return row is null ? null : Map(row);
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

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var sql = $@"
            SELECT TOP 1 {GetIngestionJobColumns(await HasSqlIdColumnAsync(connection, cancellationToken))}
            FROM [dbo].[IngestionJobs]
            WHERE [InputRef] = @InputRef
              AND [InputType] = @InputType
            ORDER BY [CreatedAtUtc] DESC;
        ";
            var row = await connection.QueryFirstOrDefaultAsync<IngestionJobRow>(
                new CommandDefinition(sql, new { InputRef = inputRef, InputType = inputType.ToString() }, cancellationToken: cancellationToken));
            return row is null ? null : Map(row);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get latest ingestion job by input ref {InputRef} and type {InputType}",
                inputRef, inputType);  // codeql[cs/log-forging]
            throw new InvalidOperationException("Failed to get latest ingestion job by input ref and type", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<IngestionJob>> GetLatestByInputRefsAsync(
        IReadOnlyCollection<(string InputRef, IngestionJobType InputType)> pairs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        if (pairs.Count == 0)
            return Array.Empty<IngestionJob>();

        using var activity = ActivitySource.StartActivity("GetLatestByInputRefs");
        activity?.SetTag("db.system", "mssql");
        activity?.SetTag("db.operation", "SELECT");
        activity?.SetTag("ingestion.pair_count", pairs.Count);

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var columns = GetIngestionJobColumns(await HasSqlIdColumnAsync(connection, cancellationToken));

            // Build a values table for the pairs
            var inputRefs = pairs.Select(p => p.InputRef).ToArray();
            var inputTypes = pairs.Select(p => p.InputType.ToString()).ToArray();

            var sql = $@"
            WITH RankedJobs AS (
                SELECT {columns},
                    ROW_NUMBER() OVER (
                        PARTITION BY [InputRef], [InputType]
                        ORDER BY [CreatedAtUtc] DESC
                    ) AS rn
                FROM [dbo].[IngestionJobs]
                WHERE [InputRef] IN @InputRefs
                  AND [InputType] IN @InputTypes
            )
            SELECT *
            FROM RankedJobs
            WHERE rn = 1;";

            var results = await connection.QueryAsync<IngestionJobRow>(
                new CommandDefinition(sql, new { InputRefs = inputRefs, InputTypes = inputTypes }, commandTimeout: 10, cancellationToken: cancellationToken));
            return results.Select(Map).ToList();
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to get latest ingestion jobs by input refs batch");
            throw new InvalidOperationException("Failed to get latest ingestion jobs by input refs batch", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<IngestionJob>> GetRecentAsync(
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        if (maxCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxCount), "maxCount must be greater than zero.");

        using var activity = ActivitySource.StartActivity("GetRecentIngestionJobs");
        activity?.SetTag("db.system", "mssql");
        activity?.SetTag("db.operation", "SELECT");
        activity?.SetTag("ingestion.max_count", maxCount);

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var sql = $@"
            SELECT TOP (@MaxCount) {GetIngestionJobColumns(await HasSqlIdColumnAsync(connection, cancellationToken))}
            FROM [dbo].[IngestionJobs]
            ORDER BY [CreatedAtUtc] DESC;
        ";
            var results = await connection.QueryAsync<IngestionJobRow>(
                new CommandDefinition(sql, new { MaxCount = maxCount }, commandTimeout: 10, cancellationToken: cancellationToken));
            return results.Select(Map).ToList();
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to get recent ingestion jobs");
            throw new InvalidOperationException("Failed to get recent ingestion jobs", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<IngestionJob>> GetByManualDocumentIdAsync(
        Guid manualDocumentId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var sql = $@"
            SELECT {GetIngestionJobColumns(await HasSqlIdColumnAsync(connection, cancellationToken))}
            FROM [dbo].[IngestionJobs]
            WHERE [ManualDocumentId] = @ManualDocumentId
            ORDER BY [CreatedAtUtc] DESC;
        ";
            var results = await connection.QueryAsync<IngestionJobRow>(
                new CommandDefinition(sql, new { ManualDocumentId = manualDocumentId }, cancellationToken: cancellationToken));

            var mapped = results.Select(Map).ToList().AsReadOnly();

            _logger.LogInformation("Retrieved {Count} ingestion jobs for manual document {ManualDocumentId}",
                mapped.Count, manualDocumentId);

            return mapped;
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

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var sql = $@"
            SELECT {GetIngestionJobColumns(await HasSqlIdColumnAsync(connection, cancellationToken))}
            FROM [dbo].[IngestionJobs]
            WHERE [Status] IN @Statuses
            ORDER BY [CreatedAtUtc] DESC;
        ";
            var results = await connection.QueryAsync<IngestionJobRow>(
                new CommandDefinition(
                    sql,
                    new { Statuses = statuses.Select(static status => status.ToString()).ToArray() },
                    cancellationToken: cancellationToken));

            return results.Select(Map).ToList().AsReadOnly();
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
                new CommandDefinition(
                    sql,
                    new { IngestionJobId = ingestionJobId },
                    commandTimeout: 60,
                    cancellationToken: cancellationToken));

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
            _logger.LogError(ex, "Failed to delete ingestion jobs for input ref {InputRef}", inputRef);  // codeql[cs/log-forging]
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
    public async Task<bool> TrySetDeletingAsync(Guid jobId, CancellationToken ct = default)
    {
        // The set of states from which a job may be transitioned into Deleting. Includes the
        // pre-processing Queued state (the job has been accepted but processing has not yet
        // started, so deletion is legitimate) alongside the terminal states. Keeping this set
        // in sync with IngestionJobService's deletable guard is what makes a Queued job that
        // passes the service check actually succeed at the atomic UPDATE. Values are derived
        // from the IngestionJobStatus enum (not hard-coded) so the persisted string
        // representations stay in sync with the domain definition.
        // AwaitingMetadata is a paused state — the job has no in-flight work, so it is safe to
        // delete. Omitting it would produce a misleading 409 when an admin deletes a job that
        // is waiting for manual metadata entry.
        var deletableStatuses = new[]
        {
            IngestionJobStatus.Queued,
            IngestionJobStatus.AwaitingMetadata,
            IngestionJobStatus.Completed,
            IngestionJobStatus.Failed,
            IngestionJobStatus.Cancelled,
            IngestionJobStatus.PartiallyCompleted
        };

        const string sql = @"
            UPDATE [dbo].[IngestionJobs] SET
                [Status] = @DeletingStatus
            WHERE [IngestionJobId] = @IngestionJobId
              AND [Status] IN @DeletableStatuses;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var affectedRows = await connection.ExecuteAsync(new CommandDefinition(sql, new
            {
                IngestionJobId = jobId,
                DeletingStatus = IngestionJobStatus.Deleting.ToString(),
                DeletableStatuses = deletableStatuses.Select(static status => status.ToString()).ToArray()
            }, cancellationToken: ct));

            var success = affectedRows > 0;
            if (success)
            {
                _logger.LogInformation(
                    "Atomically transitioned ingestion job {IngestionJobId} to Deleting",
                    jobId);
            }
            else
            {
                _logger.LogInformation(
                    "Did not transition ingestion job {IngestionJobId} to Deleting: not in a deletable terminal state or not found",
                    jobId);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to transition ingestion job {IngestionJobId} to Deleting", jobId);
            throw new InvalidOperationException($"Failed to transition ingestion job {jobId} to Deleting", ex);
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
                ingestionJobId, stage, chunksProcessed, totalChunks);  // codeql[cs/log-forging]
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

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var sql = $@"
            SELECT {GetIngestionJobColumns(await HasSqlIdColumnAsync(connection, cancellationToken))}
            FROM [dbo].[IngestionJobs]
            WHERE [DocIngestionRunId] = @DocIngestionRunId;
        ";
            var row = await connection.QueryFirstOrDefaultAsync<IngestionJobRow>(
                new CommandDefinition(sql, new { DocIngestionRunId = docIngestionRunId }, cancellationToken: cancellationToken));
            return row is null ? null : Map(row);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get ingestion job by doc ingestion run id {DocIngestionRunId}",
                docIngestionRunId);  // codeql[cs/log-forging]
            throw new InvalidOperationException($"Failed to get ingestion job by doc ingestion run id {docIngestionRunId}", ex);
        }
    }

    /// <inheritdoc/>
    public async Task UpdateMetadataAsync(
        Guid ingestionJobId,
        string? metadataJson,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE [dbo].[IngestionJobs] SET
                [MetadataJson] = @MetadataJson
            WHERE [IngestionJobId] = @IngestionJobId;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            await connection.ExecuteAsync(new CommandDefinition(sql, new
            {
                IngestionJobId = ingestionJobId,
                MetadataJson = metadataJson
            }, cancellationToken: cancellationToken));

            _logger.LogInformation(
                "Updated metadata for ingestion job {IngestionJobId} ({Length} chars).",
                ingestionJobId,
                metadataJson?.Length ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update metadata for ingestion job {IngestionJobId}", ingestionJobId);
            throw new InvalidOperationException($"Failed to update metadata for ingestion job {ingestionJobId}", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> TryTransitionFromAwaitingMetadataAsync(
        Guid ingestionJobId,
        string stage,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);

        // Compare-and-swap guard: the UPDATE only applies when the row is still in the
        // AwaitingMetadata state. This prevents lost updates: if a concurrent request or the
        // processor self-recovered between the service's read and this write, the WHERE clause
        // matches zero rows and we return false. The column-scoped SET touches only the four
        // columns relevant to the resume transition so it cannot clobber concurrent metadata
        // writes (UpdateMetadataAsync) or other stage progress.
        const string sql = @"
            UPDATE [dbo].[IngestionJobs] SET
                [Status] = @ToStatus,
                [CurrentStage] = @Stage,
                [StageSetAtUtc] = SYSUTCDATETIME(),
                [FailureReason] = NULL
            WHERE [IngestionJobId] = @IngestionJobId
              AND [Status] = @FromStatus;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var affectedRows = await connection.ExecuteAsync(new CommandDefinition(sql, new
            {
                IngestionJobId = ingestionJobId,
                FromStatus = IngestionJobStatus.AwaitingMetadata.ToString(),
                ToStatus = IngestionJobStatus.Processing.ToString(),
                Stage = stage
            }, cancellationToken: cancellationToken));

            var success = affectedRows > 0;
            if (success)
            {
                _logger.LogInformation(
                    "Atomically transitioned ingestion job {IngestionJobId} from AwaitingMetadata to Processing (resume stage: {Stage}).",
                    ingestionJobId, stage);
            }
            else
            {
                _logger.LogInformation(
                    "Did not transition ingestion job {IngestionJobId}: no longer in AwaitingMetadata state (idempotent no-op).",
                    ingestionJobId);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to transition ingestion job {IngestionJobId} from AwaitingMetadata", ingestionJobId);
            throw new InvalidOperationException($"Failed to transition ingestion job {ingestionJobId} from AwaitingMetadata", ex);
        }
    }

    /// <summary>
    /// Maps a Dapper-materialized <see cref="IngestionJobRow"/> into a fully-validated
    /// <see cref="IngestionJob"/> via the <see cref="IngestionJob.Rehydrate"/> boundary factory.
    /// Invalid database rows are rejected here rather than becoming a partially-valid entity.
    /// </summary>
    private static IngestionJob Map(IngestionJobRow row) => IngestionJob.Rehydrate(
        row.Id,
        row.IngestionJobId,
        row.CreatedAtUtc,
        row.StartedAtUtc,
        row.CompletedAtUtc,
        row.CreatedBySubject,
        row.Status,
        row.FailureReason,
        row.ErrorsJson,
        row.ErrorMessage,
        row.InputType,
        row.InputRef,
        row.SourceFileName,
        row.ComputeProvider ?? "MicrosoftFabric",
        row.DocIngestionRunId,
        row.ManualDocumentId,
        row.TotalPages,
        row.PagesCapturedViewableCount,
        row.PagesWithSearchableTextCount,
        row.PagesWithOcrTextCount,
        row.PagesWithNativeTextCount,
        row.MissingPagesJson,
        row.MetricsJson,
        row.ExpectedChunkCount,
        row.IndexedChunkCount,
        row.CurrentStage,
        row.StageSetAtUtc,
        row.MetadataJson);

    /// <summary>
    /// Private Dapper-friendly projection of the <c>dbo.IngestionJobs</c> row. Init-only so it
    /// stays a passive storage shape; it is never leaked past the Persistence boundary. The
    /// <see cref="Map"/> adapter is the only legal exit point.
    /// </summary>
    private sealed class IngestionJobRow
    {
        public long Id { get; init; }
        public Guid IngestionJobId { get; init; }
        public DateTimeOffset CreatedAtUtc { get; init; }
        public DateTimeOffset? StartedAtUtc { get; init; }
        public DateTimeOffset? CompletedAtUtc { get; init; }
        public string? CreatedBySubject { get; init; }
        public IngestionJobStatus Status { get; init; }
        public string? FailureReason { get; init; }
        public string? ErrorsJson { get; init; }
        public string? ErrorMessage { get; init; }
        public IngestionJobType InputType { get; init; }
        public string InputRef { get; init; } = string.Empty;
        public string? SourceFileName { get; init; }
        public string? ComputeProvider { get; init; }
        public string? DocIngestionRunId { get; init; }
        public Guid? ManualDocumentId { get; init; }
        public int? TotalPages { get; init; }
        public int? PagesCapturedViewableCount { get; init; }
        public int? PagesWithSearchableTextCount { get; init; }
        public int? PagesWithOcrTextCount { get; init; }
        public int? PagesWithNativeTextCount { get; init; }
        public string? MissingPagesJson { get; init; }
        public string? MetricsJson { get; init; }
        public int? ExpectedChunkCount { get; init; }
        public int? IndexedChunkCount { get; init; }
        public string? CurrentStage { get; init; }
        public DateTimeOffset? StageSetAtUtc { get; init; }
        public string? MetadataJson { get; init; }
    }
}
