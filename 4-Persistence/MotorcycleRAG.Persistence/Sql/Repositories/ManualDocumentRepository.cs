using System.Data;
using Dapper;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Entities;

namespace MotorcycleRAG.Persistence.Sql.Repositories;

public class ManualDocumentRepository : IManualDocumentRepository
{
    private const string DocumentColumns = @"
        [DocumentId], [SourceFileName], [CanonicalBlobContainer], [CanonicalBlobPath], [CanonicalBlobUri],
        [SourceContentHash], [DocumentType], [Make], [Model], [Year], [UploadedAtUtc], [CanonicalizedAtUtc],
        [LastProcessedAtUtc], [CurrentStatus], [CurrentStage], [LastSuccessfulRunId], [LastFailure]";

    private const string RunColumns = @"
        [RunId], [DocumentId], [RunType], [StartedAtUtc], [CompletedAtUtc], [Status], [StartedFromStage],
        [CompletedStage], [LocalWorkingFolder], [ProcessorHost], [ErrorSummary], [ChunkCount],
        [GraphEntityCount], [GraphRelationCount], [VectorCount]";

    private const string StageColumns = @"
        [StageId], [RunId], [StageName], [Status], [StartedAtUtc], [CompletedAtUtc], [ArtifactPath],
        [ArtifactHash], [MetadataJson], [ErrorDetail]";

    private readonly ISqlConnectionFactory _connectionFactory;
    private readonly ILogger<ManualDocumentRepository> _logger;

    public ManualDocumentRepository(ISqlConnectionFactory connectionFactory, ILogger<ManualDocumentRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<ManualDocument> CreateDocumentAsync(ManualDocument document, CancellationToken cancellationToken = default)
    {
        const string sql = $@"
            INSERT INTO [dbo].[ManualDocuments] (
                [DocumentId], [SourceFileName], [CanonicalBlobContainer], [CanonicalBlobPath], [CanonicalBlobUri],
                [SourceContentHash], [DocumentType], [Make], [Model], [Year], [UploadedAtUtc], [CurrentStatus]
            )
            VALUES (
                @DocumentId, @SourceFileName, @CanonicalBlobContainer, @CanonicalBlobPath, @CanonicalBlobUri,
                @SourceContentHash, @DocumentType, @Make, @Model, @Year, @UploadedAtUtc, @CurrentStatus
            );
            SELECT {DocumentColumns} FROM [dbo].[ManualDocuments] WHERE [DocumentId] = @DocumentId;";

        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        return await connection.QuerySingleAsync<ManualDocument>(new CommandDefinition(sql, document, cancellationToken: cancellationToken));
    }

    public async Task<ManualDocument?> GetDocumentByIdAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        const string sql = $"SELECT {DocumentColumns} FROM [dbo].[ManualDocuments] WHERE [DocumentId] = @DocumentId;";
        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        return await connection.QueryFirstOrDefaultAsync<ManualDocument>(new CommandDefinition(sql, new { DocumentId = documentId }, cancellationToken: cancellationToken));
    }

    public async Task<IEnumerable<ManualDocument>> GetAllDocumentsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = $"SELECT {DocumentColumns} FROM [dbo].[ManualDocuments] ORDER BY [UploadedAtUtc] DESC;";
        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        return await connection.QueryAsync<ManualDocument>(new CommandDefinition(sql, cancellationToken: cancellationToken));
    }

    public async Task UpdateDocumentAsync(ManualDocument document, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE [dbo].[ManualDocuments] SET
                [SourceFileName] = @SourceFileName,
                [CanonicalBlobContainer] = @CanonicalBlobContainer,
                [CanonicalBlobPath] = @CanonicalBlobPath,
                [CanonicalBlobUri] = @CanonicalBlobUri,
                [SourceContentHash] = @SourceContentHash,
                [DocumentType] = @DocumentType,
                [Make] = @Make,
                [Model] = @Model,
                [Year] = @Year,
                [CanonicalizedAtUtc] = @CanonicalizedAtUtc,
                [LastProcessedAtUtc] = @LastProcessedAtUtc,
                [CurrentStatus] = @CurrentStatus,
                [CurrentStage] = @CurrentStage,
                [LastSuccessfulRunId] = @LastSuccessfulRunId,
                [LastFailure] = @LastFailure
            WHERE [DocumentId] = @DocumentId;";

        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await connection.ExecuteAsync(new CommandDefinition(sql, document, cancellationToken: cancellationToken));
    }

    public async Task<ManualProcessingRun> CreateRunAsync(ManualProcessingRun run, CancellationToken cancellationToken = default)
    {
        const string sql = $@"
            INSERT INTO [dbo].[ManualProcessingRuns] (
                [RunId], [DocumentId], [RunType], [StartedAtUtc], [Status], [StartedFromStage], [LocalWorkingFolder]
            )
            VALUES (
                @RunId, @DocumentId, @RunType, @StartedAtUtc, @Status, @StartedFromStage, @LocalWorkingFolder
            );
            SELECT {RunColumns} FROM [dbo].[ManualProcessingRuns] WHERE [RunId] = @RunId;";

        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        return await connection.QuerySingleAsync<ManualProcessingRun>(new CommandDefinition(sql, run, cancellationToken: cancellationToken));
    }

    public async Task<ManualProcessingRun?> GetRunByIdAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        const string sql = $"SELECT {RunColumns} FROM [dbo].[ManualProcessingRuns] WHERE [RunId] = @RunId;";
        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        return await connection.QueryFirstOrDefaultAsync<ManualProcessingRun>(new CommandDefinition(sql, new { RunId = runId }, cancellationToken: cancellationToken));
    }

    public async Task<IEnumerable<ManualProcessingRun>> GetRunsForDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        const string sql = $"SELECT {RunColumns} FROM [dbo].[ManualProcessingRuns] WHERE [DocumentId] = @DocumentId ORDER BY [StartedAtUtc] DESC;";
        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        return await connection.QueryAsync<ManualProcessingRun>(new CommandDefinition(sql, new { DocumentId = documentId }, cancellationToken: cancellationToken));
    }

    public async Task UpdateRunAsync(ManualProcessingRun run, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE [dbo].[ManualProcessingRuns] SET
                [CompletedAtUtc] = @CompletedAtUtc,
                [Status] = @Status,
                [CompletedStage] = @CompletedStage,
                [LocalWorkingFolder] = @LocalWorkingFolder,
                [ProcessorHost] = @ProcessorHost,
                [ErrorSummary] = @ErrorSummary,
                [ChunkCount] = @ChunkCount,
                [GraphEntityCount] = @GraphEntityCount,
                [GraphRelationCount] = @GraphRelationCount,
                [VectorCount] = @VectorCount
            WHERE [RunId] = @RunId;";

        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await connection.ExecuteAsync(new CommandDefinition(sql, run, cancellationToken: cancellationToken));
    }

    public async Task<ManualProcessingStage> CreateStageAsync(ManualProcessingStage stage, CancellationToken cancellationToken = default)
    {
        const string sql = $@"
            INSERT INTO [dbo].[ManualProcessingStages] (
                [StageId], [RunId], [StageName], [Status], [StartedAtUtc], [MetadataJson]
            )
            VALUES (
                @StageId, @RunId, @StageName, @Status, @StartedAtUtc, @MetadataJson
            );
            SELECT {StageColumns} FROM [dbo].[ManualProcessingStages] WHERE [StageId] = @StageId;";

        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        return await connection.QuerySingleAsync<ManualProcessingStage>(new CommandDefinition(sql, stage, cancellationToken: cancellationToken));
    }

    public async Task<IEnumerable<ManualProcessingStage>> GetStagesForRunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        const string sql = $"SELECT {StageColumns} FROM [dbo].[ManualProcessingStages] WHERE [RunId] = @RunId ORDER BY [StartedAtUtc] ASC;";
        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        return await connection.QueryAsync<ManualProcessingStage>(new CommandDefinition(sql, new { RunId = runId }, cancellationToken: cancellationToken));
    }

    public async Task UpdateStageAsync(ManualProcessingStage stage, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE [dbo].[ManualProcessingStages] SET
                [Status] = @Status,
                [CompletedAtUtc] = @CompletedAtUtc,
                [ArtifactPath] = @ArtifactPath,
                [ArtifactHash] = @ArtifactHash,
                [MetadataJson] = @MetadataJson,
                [ErrorDetail] = @ErrorDetail
            WHERE [StageId] = @StageId;";

        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await connection.ExecuteAsync(new CommandDefinition(sql, stage, cancellationToken: cancellationToken));
    }

    public async Task<IEnumerable<(ManualDocument Document, ManualProcessingRun Run)>> GetRecentManualOperationsAsync(int top, CancellationToken cancellationToken = default)
    {
        var sql = $@"
            SELECT TOP (@Top)
                d.[DocumentId], d.[SourceFileName], d.[CanonicalBlobContainer], d.[CanonicalBlobPath], d.[CanonicalBlobUri],
                d.[SourceContentHash], d.[DocumentType], d.[Make], d.[Model], d.[Year], d.[UploadedAtUtc], d.[CanonicalizedAtUtc],
                d.[LastProcessedAtUtc], d.[CurrentStatus], d.[CurrentStage], d.[LastSuccessfulRunId], d.[LastFailure],
                r.[RunId], r.[DocumentId], r.[RunType], r.[StartedAtUtc], r.[CompletedAtUtc], r.[Status], r.[StartedFromStage],
                r.[CompletedStage], r.[LocalWorkingFolder], r.[ProcessorHost], r.[ErrorSummary], r.[ChunkCount],
                r.[GraphEntityCount], r.[GraphRelationCount], r.[VectorCount]
            FROM [dbo].[ManualProcessingRuns] r
            INNER JOIN [dbo].[ManualDocuments] d ON r.[DocumentId] = d.[DocumentId]
            ORDER BY r.[StartedAtUtc] DESC;";

        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        var results = await connection.QueryAsync<ManualDocument, ManualProcessingRun, (ManualDocument, ManualProcessingRun)>(
            new CommandDefinition(sql, new { Top = top }, cancellationToken: cancellationToken),
            (doc, run) => (doc, run),
            splitOn: "RunId");

        return results;
    }
}
