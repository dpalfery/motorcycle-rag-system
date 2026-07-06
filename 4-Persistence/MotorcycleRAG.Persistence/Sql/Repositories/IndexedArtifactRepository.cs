using Dapper;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;
using MotorcycleRAG.Persistence.Sql;

namespace MotorcycleRAG.Persistence.Sql.Repositories;

public class IndexedArtifactRepository : IIndexedArtifactRepository
{
    private readonly ISqlConnectionFactory _connectionFactory;
    private readonly ILogger<IndexedArtifactRepository> _logger;

    public IndexedArtifactRepository(ISqlConnectionFactory connectionFactory, ILogger<IndexedArtifactRepository> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IndexedArtifact> UpsertAsync(IndexedArtifact artifact, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        const string sql = @"
            MERGE [dbo].[IndexedArtifacts] AS target
            USING (SELECT @UploadId AS UploadId, @ArtifactType AS ArtifactType) AS source
            ON target.[UploadId] = source.UploadId AND target.[ArtifactType] = source.ArtifactType
            WHEN MATCHED THEN
                UPDATE SET
                    [State] = @State,
                    [ExpectedChunkCount] = @ExpectedChunkCount,
                    [IndexedChunkCount] = @IndexedChunkCount,
                    [FailedChunkCount] = @FailedChunkCount,
                    [LastProcessedAtUtc] = @LastProcessedAtUtc,
                    [FailureReason] = @FailureReason,
                    [UpdatedAtUtc] = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT ([IndexedArtifactId], [IngestionJobId], [UploadId], [ArtifactType],
                        [BlobContainer], [BlobPath], [SourceFileName], [State],
                        [ExpectedChunkCount], [IndexedChunkCount], [FailedChunkCount],
                        [LastProcessedAtUtc], [FailureReason], [CreatedAtUtc])
                VALUES (@IndexedArtifactId, @IngestionJobId, @UploadId, @ArtifactType,
                        @BlobContainer, @BlobPath, @SourceFileName, @State,
                        @ExpectedChunkCount, @IndexedChunkCount, @FailedChunkCount,
                        @LastProcessedAtUtc, @FailureReason, SYSUTCDATETIME());
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            await connection.ExecuteAsync(new CommandDefinition(sql, new
            {
                artifact.IndexedArtifactId,
                artifact.IngestionJobId,
                artifact.UploadId,
                artifact.ArtifactType,
                artifact.BlobContainer,
                artifact.BlobPath,
                artifact.SourceFileName,
                State = artifact.State.ToString(),
                artifact.ExpectedChunkCount,
                artifact.IndexedChunkCount,
                artifact.FailedChunkCount,
                artifact.LastProcessedAtUtc,
                artifact.FailureReason
            }, cancellationToken: cancellationToken));

            return artifact;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upsert indexed artifact {UploadId}", artifact.UploadId);
            throw new InvalidOperationException($"Failed to upsert indexed artifact", ex);
        }
    }

    public async Task<IndexedArtifact?> GetByIdAsync(Guid artifactId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT [IndexedArtifactId], [IngestionJobId], [UploadId], [ArtifactType],
                   [BlobContainer], [BlobPath], [SourceFileName], [State],
                   [ExpectedChunkCount], [IndexedChunkCount], [FailedChunkCount],
                   [LastProcessedAtUtc], [FailureReason], [CreatedAtUtc], [UpdatedAtUtc]
            FROM [dbo].[IndexedArtifacts]
            WHERE [IndexedArtifactId] = @IndexedArtifactId;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var row = await connection.QueryFirstOrDefaultAsync<dynamic>(
                new CommandDefinition(sql, new { IndexedArtifactId = artifactId }, cancellationToken: cancellationToken));

            return row == null ? null : MapToEntity(row);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get indexed artifact {ArtifactId}", artifactId);
            throw new InvalidOperationException($"Failed to get indexed artifact", ex);
        }
    }

    public async Task<IndexedArtifact?> GetByUploadAndTypeAsync(string uploadId, string artifactType, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT [IndexedArtifactId], [IngestionJobId], [UploadId], [ArtifactType],
                   [BlobContainer], [BlobPath], [SourceFileName], [State],
                   [ExpectedChunkCount], [IndexedChunkCount], [FailedChunkCount],
                   [LastProcessedAtUtc], [FailureReason], [CreatedAtUtc], [UpdatedAtUtc]
            FROM [dbo].[IndexedArtifacts]
            WHERE [UploadId] = @UploadId AND [ArtifactType] = @ArtifactType;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var row = await connection.QueryFirstOrDefaultAsync<dynamic>(
                new CommandDefinition(sql, new { UploadId = uploadId, ArtifactType = artifactType }, cancellationToken: cancellationToken));

            return row == null ? null : MapToEntity(row);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get indexed artifact by upload {UploadId}", uploadId);
            throw new InvalidOperationException($"Failed to get indexed artifact", ex);
        }
    }

    public async Task<IReadOnlyList<IndexedArtifact>> GetByStatesAsync(IReadOnlyCollection<IndexedArtifactState> states, CancellationToken cancellationToken = default)
    {
        if (states.Count == 0) return [];

        var stateStrings = states.Select(s => s.ToString()).ToList();
        const string sql = @"
            SELECT [IndexedArtifactId], [IngestionJobId], [UploadId], [ArtifactType],
                   [BlobContainer], [BlobPath], [SourceFileName], [State],
                   [ExpectedChunkCount], [IndexedChunkCount], [FailedChunkCount],
                   [LastProcessedAtUtc], [FailureReason], [CreatedAtUtc], [UpdatedAtUtc]
            FROM [dbo].[IndexedArtifacts]
            WHERE [State] IN @States
            ORDER BY [CreatedAtUtc] DESC;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var rows = await connection.QueryAsync<dynamic>(
                new CommandDefinition(sql, new { States = stateStrings }, cancellationToken: cancellationToken));

            return rows.Select(MapToEntity).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get indexed artifacts by states");
            throw new InvalidOperationException($"Failed to get indexed artifacts", ex);
        }
    }

    public async Task<IReadOnlyList<IndexedArtifact>> GetAllAsync(int maxCount = 1000, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT TOP (@MaxCount) [IndexedArtifactId], [IngestionJobId], [UploadId], [ArtifactType],
                   [BlobContainer], [BlobPath], [SourceFileName], [State],
                   [ExpectedChunkCount], [IndexedChunkCount], [FailedChunkCount],
                   [LastProcessedAtUtc], [FailureReason], [CreatedAtUtc], [UpdatedAtUtc]
            FROM [dbo].[IndexedArtifacts]
            ORDER BY [CreatedAtUtc] DESC;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var rows = await connection.QueryAsync<dynamic>(
                new CommandDefinition(sql, new { MaxCount = maxCount }, cancellationToken: cancellationToken));

            return rows.Select(MapToEntity).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all indexed artifacts");
            throw new InvalidOperationException($"Failed to get indexed artifacts", ex);
        }
    }

    public async Task<IReadOnlyList<IndexedArtifact>> GetByIngestionJobIdAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT [IndexedArtifactId], [IngestionJobId], [UploadId], [ArtifactType],
                   [BlobContainer], [BlobPath], [SourceFileName], [State],
                   [ExpectedChunkCount], [IndexedChunkCount], [FailedChunkCount],
                   [LastProcessedAtUtc], [FailureReason], [CreatedAtUtc], [UpdatedAtUtc]
            FROM [dbo].[IndexedArtifacts]
            WHERE [IngestionJobId] = @IngestionJobId;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var rows = await connection.QueryAsync<dynamic>(
                new CommandDefinition(sql, new { IngestionJobId = jobId }, cancellationToken: cancellationToken));

            return rows.Select(MapToEntity).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get indexed artifacts by job {JobId}", jobId);
            throw new InvalidOperationException($"Failed to get indexed artifacts", ex);
        }
    }

    public async Task<IReadOnlyList<IndexedArtifact>> GetByUploadIdAsync(string uploadId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uploadId);

        const string sql = @"
            SELECT [IndexedArtifactId], [IngestionJobId], [UploadId], [ArtifactType],
                   [BlobContainer], [BlobPath], [SourceFileName], [State],
                   [ExpectedChunkCount], [IndexedChunkCount], [FailedChunkCount],
                   [LastProcessedAtUtc], [FailureReason], [CreatedAtUtc], [UpdatedAtUtc]
            FROM [dbo].[IndexedArtifacts]
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
            _logger.LogError(ex, "Failed to get indexed artifacts by upload {UploadId}", uploadId);
            throw new InvalidOperationException("Failed to get indexed artifacts", ex);
        }
    }

    public async Task DeleteByIdAsync(Guid artifactId, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM [dbo].[IndexedArtifacts] WHERE [IndexedArtifactId] = @IndexedArtifactId;";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            await connection.ExecuteAsync(new CommandDefinition(sql, new { IndexedArtifactId = artifactId }, cancellationToken: cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete indexed artifact {ArtifactId}", artifactId);
            throw new InvalidOperationException("Failed to delete indexed artifact", ex);
        }
    }

    private static IndexedArtifact MapToEntity(dynamic row)
    {
        return new IndexedArtifact
        {
            IndexedArtifactId = row.IndexedArtifactId,
            IngestionJobId = row.IngestionJobId,
            UploadId = row.UploadId,
            ArtifactType = row.ArtifactType,
            BlobContainer = row.BlobContainer,
            BlobPath = row.BlobPath,
            SourceFileName = row.SourceFileName,
            State = Enum.Parse<IndexedArtifactState>(row.State),
            ExpectedChunkCount = row.ExpectedChunkCount,
            IndexedChunkCount = row.IndexedChunkCount,
            FailedChunkCount = row.FailedChunkCount,
            LastProcessedAtUtc = row.LastProcessedAtUtc,
            FailureReason = row.FailureReason,
            CreatedAtUtc = row.CreatedAtUtc,
            UpdatedAtUtc = row.UpdatedAtUtc
        };
    }
}
