using Dapper;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;
using MotorcycleRAG.Persistence.Sql;

namespace MotorcycleRAG.Persistence.Sql.Repositories;

public class IndexedChunkRepository : IIndexedChunkRepository
{
    private readonly ISqlConnectionFactory _connectionFactory;
    private readonly ILogger<IndexedChunkRepository> _logger;

    public IndexedChunkRepository(ISqlConnectionFactory connectionFactory, ILogger<IndexedChunkRepository> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task UpsertManyAsync(IReadOnlyCollection<IndexedChunk> chunks, CancellationToken cancellationToken = default)
    {
        if (chunks.Count == 0) return;

        const string sql = @"
            MERGE [dbo].[IndexedChunks] AS target
            USING (SELECT @ChunkId AS ChunkId, @IndexedArtifactId AS IndexedArtifactId,
                         @IngestionJobId AS IngestionJobId, @UploadId AS UploadId,
                         @SourceFileName AS SourceFileName, @PageNumber AS PageNumber,
                         @ChunkIndex AS ChunkIndex, @Stage AS Stage, @Status AS Status,
                         @ProcessedAtUtc AS ProcessedAtUtc, @FailureReason AS FailureReason) AS source
            ON target.[ChunkId] = source.ChunkId
            WHEN MATCHED THEN
                UPDATE SET
                    [Status] = source.Status,
                    [ProcessedAtUtc] = source.ProcessedAtUtc,
                    [FailureReason] = source.FailureReason
            WHEN NOT MATCHED THEN
                INSERT ([ChunkId], [IndexedArtifactId], [IngestionJobId], [UploadId],
                        [SourceFileName], [PageNumber], [ChunkIndex], [Stage], [Status],
                        [ProcessedAtUtc], [FailureReason])
                VALUES (source.ChunkId, source.IndexedArtifactId, source.IngestionJobId, source.UploadId,
                        source.SourceFileName, source.PageNumber, source.ChunkIndex, source.Stage, source.Status,
                        source.ProcessedAtUtc, source.FailureReason);
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            foreach (var chunk in chunks)
            {
                await connection.ExecuteAsync(new CommandDefinition(sql, new
                {
                    chunk.ChunkId,
                    chunk.IndexedArtifactId,
                    chunk.IngestionJobId,
                    chunk.UploadId,
                    chunk.SourceFileName,
                    chunk.PageNumber,
                    chunk.ChunkIndex,
                    chunk.Stage,
                    Status = chunk.Status.ToString(),
                    chunk.ProcessedAtUtc,
                    chunk.FailureReason
                }, cancellationToken: cancellationToken));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upsert {Count} indexed chunks", chunks.Count);
            throw new InvalidOperationException($"Failed to upsert indexed chunks", ex);
        }
    }

    public async Task<IReadOnlyList<IndexedChunk>> GetByArtifactIdAsync(Guid artifactId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT [ChunkId], [IndexedArtifactId], [IngestionJobId], [UploadId],
                   [SourceFileName], [PageNumber], [ChunkIndex], [Stage], [Status],
                   [ProcessedAtUtc], [FailureReason]
            FROM [dbo].[IndexedChunks]
            WHERE [IndexedArtifactId] = @IndexedArtifactId;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var rows = await connection.QueryAsync<dynamic>(
                new CommandDefinition(sql, new { IndexedArtifactId = artifactId }, cancellationToken: cancellationToken));

            return rows.Select(MapToEntity).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get chunks by artifact {ArtifactId}", artifactId);
            throw new InvalidOperationException($"Failed to get indexed chunks", ex);
        }
    }

    public async Task<IReadOnlyList<IndexedChunk>> GetByIngestionJobIdAsync(Guid ingestionJobId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT [ChunkId], [IndexedArtifactId], [IngestionJobId], [UploadId],
                   [SourceFileName], [PageNumber], [ChunkIndex], [Stage], [Status],
                   [ProcessedAtUtc], [FailureReason]
            FROM [dbo].[IndexedChunks]
            WHERE [IngestionJobId] = @IngestionJobId;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var rows = await connection.QueryAsync<dynamic>(
                new CommandDefinition(sql, new { IngestionJobId = ingestionJobId }, cancellationToken: cancellationToken));

            return rows.Select(MapToEntity).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get chunks by ingestion job {IngestionJobId}", ingestionJobId);
            throw new InvalidOperationException("Failed to get indexed chunks", ex);
        }
    }

    public async Task<IReadOnlyList<IndexedChunk>> GetByUploadIdAsync(string uploadId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uploadId);

        const string sql = @"
            SELECT [ChunkId], [IndexedArtifactId], [IngestionJobId], [UploadId],
                   [SourceFileName], [PageNumber], [ChunkIndex], [Stage], [Status],
                   [ProcessedAtUtc], [FailureReason]
            FROM [dbo].[IndexedChunks]
            WHERE [UploadId] = @UploadId;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var rows = await connection.QueryAsync<dynamic>(
                new CommandDefinition(sql, new { UploadId = uploadId }, cancellationToken: cancellationToken));

            return rows.Select(MapToEntity).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get chunks by upload {UploadId}", uploadId);
            throw new InvalidOperationException("Failed to get indexed chunks", ex);
        }
    }

    public async Task DeleteByArtifactIdAsync(Guid artifactId, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM [dbo].[IndexedChunks] WHERE [IndexedArtifactId] = @IndexedArtifactId;";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            await connection.ExecuteAsync(new CommandDefinition(sql, new { IndexedArtifactId = artifactId }, cancellationToken: cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete chunks for artifact {ArtifactId}", artifactId);
            throw new InvalidOperationException($"Failed to delete indexed chunks", ex);
        }
    }

    public async Task DeleteByIngestionJobIdAsync(Guid ingestionJobId, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM [dbo].[IndexedChunks] WHERE [IngestionJobId] = @IngestionJobId;";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            await connection.ExecuteAsync(new CommandDefinition(sql, new { IngestionJobId = ingestionJobId }, cancellationToken: cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete chunks for ingestion job {IngestionJobId}", ingestionJobId);
            throw new InvalidOperationException("Failed to delete indexed chunks", ex);
        }
    }

    public async Task DeleteByUploadIdAsync(string uploadId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uploadId);

        const string sql = "DELETE FROM [dbo].[IndexedChunks] WHERE [UploadId] = @UploadId;";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            await connection.ExecuteAsync(new CommandDefinition(sql, new { UploadId = uploadId }, cancellationToken: cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete chunks for upload {UploadId}", uploadId);
            throw new InvalidOperationException("Failed to delete indexed chunks", ex);
        }
    }

    public async Task<int> CountByArtifactIdAsync(Guid artifactId, CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT COUNT(*) FROM [dbo].[IndexedChunks] WHERE [IndexedArtifactId] = @IndexedArtifactId;";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            return await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(sql, new { IndexedArtifactId = artifactId }, cancellationToken: cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to count chunks for artifact {ArtifactId}", artifactId);
            throw new InvalidOperationException($"Failed to count indexed chunks", ex);
        }
    }

    public async Task<int> CountByStatusAsync(Guid artifactId, string status, CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT COUNT(*) FROM [dbo].[IndexedChunks] WHERE [IndexedArtifactId] = @IndexedArtifactId AND [Status] = @Status;";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            return await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(sql, new { IndexedArtifactId = artifactId, Status = status }, cancellationToken: cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to count chunks by status for artifact {ArtifactId}", artifactId);
            throw new InvalidOperationException($"Failed to count indexed chunks", ex);
        }
    }

    private static IndexedChunk MapToEntity(dynamic row)
    {
        return new IndexedChunk
        {
            ChunkId = row.ChunkId,
            IndexedArtifactId = row.IndexedArtifactId,
            IngestionJobId = row.IngestionJobId,
            UploadId = row.UploadId,
            SourceFileName = row.SourceFileName,
            PageNumber = row.PageNumber,
            ChunkIndex = row.ChunkIndex,
            Stage = row.Stage,
            Status = Enum.Parse<ChunkIndexStatus>(row.Status),
            ProcessedAtUtc = row.ProcessedAtUtc,
            FailureReason = row.FailureReason
        };
    }
}
