using System.Data;
using Dapper;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs.ManualIngestion;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;

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
        var row = await connection.QuerySingleAsync<ManualDocumentRow>(new CommandDefinition(sql, document, cancellationToken: cancellationToken));
        return Map(row);
    }

    public async Task<ManualDocument?> GetDocumentByIdAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        const string sql = $"SELECT {DocumentColumns} FROM [dbo].[ManualDocuments] WHERE [DocumentId] = @DocumentId;";
        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        var row = await connection.QueryFirstOrDefaultAsync<ManualDocumentRow>(new CommandDefinition(sql, new { DocumentId = documentId }, cancellationToken: cancellationToken));
        return row is null ? null : Map(row);
    }

    public async Task<IEnumerable<ManualDocument>> GetAllDocumentsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = $"SELECT {DocumentColumns} FROM [dbo].[ManualDocuments] ORDER BY [UploadedAtUtc] DESC;";
        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        var rows = await connection.QueryAsync<ManualDocumentRow>(new CommandDefinition(sql, cancellationToken: cancellationToken));
        return rows.Select(Map).ToList();
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

    public async Task<ManualRunDto> CreateRunAsync(ManualRunDto run, CancellationToken cancellationToken = default)
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
        return await connection.QuerySingleAsync<ManualRunDto>(new CommandDefinition(sql, run, cancellationToken: cancellationToken));
    }

    public async Task<ManualRunDto?> GetRunByIdAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        const string sql = $"SELECT {RunColumns} FROM [dbo].[ManualProcessingRuns] WHERE [RunId] = @RunId;";
        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        return await connection.QueryFirstOrDefaultAsync<ManualRunDto>(new CommandDefinition(sql, new { RunId = runId }, cancellationToken: cancellationToken));
    }

    public async Task<IEnumerable<ManualRunDto>> GetRunsForDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        const string sql = $"SELECT {RunColumns} FROM [dbo].[ManualProcessingRuns] WHERE [DocumentId] = @DocumentId ORDER BY [StartedAtUtc] DESC;";
        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        return await connection.QueryAsync<ManualRunDto>(new CommandDefinition(sql, new { DocumentId = documentId }, cancellationToken: cancellationToken));
    }

    public async Task UpdateRunAsync(ManualRunDto run, CancellationToken cancellationToken = default)
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

    public async Task<ManualStageDto> CreateStageAsync(ManualStageDto stage, CancellationToken cancellationToken = default)
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
        return await connection.QuerySingleAsync<ManualStageDto>(new CommandDefinition(sql, stage, cancellationToken: cancellationToken));
    }

    public async Task<IEnumerable<ManualStageDto>> GetStagesForRunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        const string sql = $"SELECT {StageColumns} FROM [dbo].[ManualProcessingStages] WHERE [RunId] = @RunId ORDER BY [StartedAtUtc] ASC;";
        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        return await connection.QueryAsync<ManualStageDto>(new CommandDefinition(sql, new { RunId = runId }, cancellationToken: cancellationToken));
    }

    public async Task UpdateStageAsync(ManualStageDto stage, CancellationToken cancellationToken = default)
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

    public async Task<IEnumerable<(ManualDocument Document, ManualRunDto Run)>> GetRecentManualOperationsAsync(int top, CancellationToken cancellationToken = default)
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
        var results = await connection.QueryAsync<ManualDocumentRow, ManualRunDto, (ManualDocumentRow Document, ManualRunDto Run)>(
            new CommandDefinition(sql, new { Top = top }, cancellationToken: cancellationToken),
            (doc, run) => (doc, run),
            splitOn: "RunId");

        return results.Select(x => (Document: Map(x.Document), x.Run));
    }

    private static ManualDocument Map(ManualDocumentRow row) =>
        ManualDocument.Rehydrate(
            row.DocumentId,
            row.SourceFileName,
            row.CanonicalBlobContainer,
            row.CanonicalBlobPath,
            row.CanonicalBlobUri,
            row.SourceContentHash,
            row.DocumentType,
            row.Make,
            row.Model,
            row.Year,
            row.UploadedAtUtc,
            row.CanonicalizedAtUtc,
            row.LastProcessedAtUtc,
            row.CurrentStatus,
            row.CurrentStage,
            row.LastSuccessfulRunId,
            row.LastFailure);

    private sealed class ManualDocumentRow
    {
        public Guid DocumentId { get; init; }
        public string SourceFileName { get; init; } = string.Empty;
        public string CanonicalBlobContainer { get; init; } = string.Empty;
        public string CanonicalBlobPath { get; init; } = string.Empty;
        public string? CanonicalBlobUri { get; init; }
        public string? SourceContentHash { get; init; }
        public string DocumentType { get; init; } = string.Empty;
        public string? Make { get; init; }
        public string? Model { get; init; }
        public int? Year { get; init; }
        public DateTimeOffset UploadedAtUtc { get; init; }
        public DateTimeOffset? CanonicalizedAtUtc { get; init; }
        public DateTimeOffset? LastProcessedAtUtc { get; init; }
        public MotorcycleRAG.Domain.Enums.ManualDocumentStatus CurrentStatus { get; init; }
        public string? CurrentStage { get; init; }
        public Guid? LastSuccessfulRunId { get; init; }
        public string? LastFailure { get; init; }
    }
}
