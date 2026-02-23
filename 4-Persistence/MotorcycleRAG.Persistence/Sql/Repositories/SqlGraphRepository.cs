using Dapper;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Repositories;
using MotorcycleRAG.Domain.Entities;

namespace MotorcycleRAG.Persistence.Sql.Repositories;

/// <summary>
/// Dapper-based implementation of <see cref="IGraphRepository"/> using SQL Server Graph tables.
/// All SQL uses parameterized queries — no string concatenation.
/// </summary>
/// <remarks>
/// SQL Server Graph AS EDGE tables use $from_id/$to_id internally.
/// This implementation uses explicit <c>FromNodeId</c>/<c>ToNodeId</c> GUID columns
/// added alongside the AS EDGE syntax in <c>GraphTablesMigration.sql</c>.
/// If those columns are missing from the schema, apply the migration first.
/// </remarks>
public class SqlGraphRepository : IGraphRepository
{
    private readonly ISqlConnectionFactory _connectionFactory;
    private readonly ILogger<SqlGraphRepository> _logger;

    public SqlGraphRepository(
        ISqlConnectionFactory connectionFactory,
        ILogger<SqlGraphRepository> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task UpsertNodeAsync(GraphNode node, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);

        const string sql = @"
            MERGE [dbo].[GraphNode] AS target
            USING (SELECT @Id AS [Id]) AS source ON target.[Id] = source.[Id]
            WHEN MATCHED THEN
                UPDATE SET
                    [Name]             = @Name,
                    [Type]             = @Type,
                    [Description]      = @Description,
                    [SourceDocumentId] = @SourceDocumentId,
                    [UpdatedAtUtc]     = @UpdatedAtUtc
            WHEN NOT MATCHED THEN
                INSERT ([Id], [Name], [Type], [Description], [SourceDocumentId], [CreatedAtUtc])
                VALUES (@Id,  @Name,  @Type,  @Description,  @SourceDocumentId,  @CreatedAtUtc);
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            await connection.ExecuteAsync(new CommandDefinition(sql, new
            {
                node.Id,
                node.Name,
                node.Type,
                node.Description,
                node.SourceDocumentId,
                CreatedAtUtc = node.CreatedAtUtc.UtcDateTime,
                UpdatedAtUtc = node.UpdatedAtUtc?.UtcDateTime ?? DateTime.UtcNow
            }, cancellationToken: cancellationToken));

            _logger.LogInformation("Upserted graph node {NodeId}", node.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upsert graph node {NodeId}", node.Id);
            throw new InvalidOperationException($"Failed to upsert graph node {node.Id}", ex);
        }
    }

    /// <inheritdoc/>
    public async Task UpsertNodesAsync(
        IReadOnlyList<GraphNode> nodes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        if (nodes.Count == 0) return;

        const string sql = @"
            MERGE [dbo].[GraphNode] AS target
            USING (SELECT @Id AS [Id]) AS source ON target.[Id] = source.[Id]
            WHEN MATCHED THEN
                UPDATE SET
                    [Name]             = @Name,
                    [Type]             = @Type,
                    [Description]      = @Description,
                    [SourceDocumentId] = @SourceDocumentId,
                    [UpdatedAtUtc]     = @UpdatedAtUtc
            WHEN NOT MATCHED THEN
                INSERT ([Id], [Name], [Type], [Description], [SourceDocumentId], [CreatedAtUtc])
                VALUES (@Id,  @Name,  @Type,  @Description,  @SourceDocumentId,  @CreatedAtUtc);
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            using var transaction = connection.BeginTransaction();
            try
            {
                foreach (var node in nodes)
                {
                    await connection.ExecuteAsync(new CommandDefinition(sql, new
                    {
                        node.Id,
                        node.Name,
                        node.Type,
                        node.Description,
                        node.SourceDocumentId,
                        CreatedAtUtc = node.CreatedAtUtc.UtcDateTime,
                        UpdatedAtUtc = node.UpdatedAtUtc?.UtcDateTime ?? DateTime.UtcNow
                    }, transaction, cancellationToken: cancellationToken));
                }
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }

            _logger.LogInformation("Upserted {Count} graph nodes in batch", nodes.Count);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upsert {Count} graph nodes in batch", nodes.Count);
            throw new InvalidOperationException($"Failed to upsert {nodes.Count} graph nodes in batch", ex);
        }
    }

    /// <inheritdoc/>
    public async Task UpsertEdgeAsync(GraphEdge edge, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edge);

        // TODO: SQL Server Graph AS EDGE tables use $from_id/$to_id internally.
        // This implementation uses explicit FromNodeId/ToNodeId columns added to the AS EDGE table.
        // See GraphTablesMigration.sql — if those columns are absent, apply the schema migration first.
        const string sql = @"
            IF NOT EXISTS (
                SELECT 1 FROM [dbo].[GraphEdge]
                WHERE [RelationshipType] = @RelationshipType
                  AND [FromNodeId]       = @FromNodeId
                  AND [ToNodeId]         = @ToNodeId
            )
            INSERT INTO [dbo].[GraphEdge]
                ([RelationshipType], [Weight], [Context], [CreatedAtUtc], [FromNodeId], [ToNodeId])
            VALUES
                (@RelationshipType,  @Weight,  @Context,  @CreatedAtUtc,  @FromNodeId,  @ToNodeId);
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            await connection.ExecuteAsync(new CommandDefinition(sql, new
            {
                edge.RelationshipType,
                edge.Weight,
                edge.Context,
                CreatedAtUtc = edge.CreatedAtUtc.UtcDateTime,
                edge.FromNodeId,
                edge.ToNodeId
            }, cancellationToken: cancellationToken));

            _logger.LogInformation("Upserted graph edge {RelationshipType} from {FromNodeId} to {ToNodeId}",
                edge.RelationshipType, edge.FromNodeId, edge.ToNodeId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upsert graph edge from {FromNodeId} to {ToNodeId}",
                edge.FromNodeId, edge.ToNodeId);
            throw new InvalidOperationException(
                $"Failed to upsert graph edge from {edge.FromNodeId} to {edge.ToNodeId}", ex);
        }
    }

    /// <inheritdoc/>
    public async Task UpsertEdgesAsync(
        IReadOnlyList<GraphEdge> edges,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edges);
        if (edges.Count == 0) return;

        const string sql = @"
            IF NOT EXISTS (
                SELECT 1 FROM [dbo].[GraphEdge]
                WHERE [RelationshipType] = @RelationshipType
                  AND [FromNodeId]       = @FromNodeId
                  AND [ToNodeId]         = @ToNodeId
            )
            INSERT INTO [dbo].[GraphEdge]
                ([RelationshipType], [Weight], [Context], [CreatedAtUtc], [FromNodeId], [ToNodeId])
            VALUES
                (@RelationshipType,  @Weight,  @Context,  @CreatedAtUtc,  @FromNodeId,  @ToNodeId);
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            using var transaction = connection.BeginTransaction();
            try
            {
                foreach (var edge in edges)
                {
                    await connection.ExecuteAsync(new CommandDefinition(sql, new
                    {
                        edge.RelationshipType,
                        edge.Weight,
                        edge.Context,
                        CreatedAtUtc = edge.CreatedAtUtc.UtcDateTime,
                        edge.FromNodeId,
                        edge.ToNodeId
                    }, transaction, cancellationToken: cancellationToken));
                }
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }

            _logger.LogInformation("Upserted {Count} graph edges in batch", edges.Count);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upsert {Count} graph edges in batch", edges.Count);
            throw new InvalidOperationException($"Failed to upsert {edges.Count} graph edges in batch", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<GraphNode>> GetNodesByDocumentAsync(
        Guid sourceDocumentId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT [Id], [Name], [Type], [Description], [SourceDocumentId], [CreatedAtUtc], [UpdatedAtUtc]
            FROM   [dbo].[GraphNode]
            WHERE  [SourceDocumentId] = @SourceDocumentId
            ORDER BY [Name];
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var results = await connection.QueryAsync<GraphNode>(
                new CommandDefinition(sql, new { SourceDocumentId = sourceDocumentId },
                    cancellationToken: cancellationToken));

            var list = results.ToList().AsReadOnly();
            _logger.LogInformation("Retrieved {Count} graph nodes for document {SourceDocumentId}",
                list.Count, sourceDocumentId);
            return list;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get graph nodes for document {SourceDocumentId}", sourceDocumentId);
            throw new InvalidOperationException(
                $"Failed to get graph nodes for document {sourceDocumentId}", ex);
        }
    }

    /// <inheritdoc/>
    public async Task DeleteByDocumentAsync(
        Guid sourceDocumentId,
        CancellationToken cancellationToken = default)
    {
        // Delete edges that reference any node belonging to this document, then delete the nodes.
        const string sql = @"
            DELETE FROM [dbo].[GraphEdge]
            WHERE [FromNodeId] IN (
                SELECT [Id] FROM [dbo].[GraphNode] WHERE [SourceDocumentId] = @SourceDocumentId
            )
            OR [ToNodeId] IN (
                SELECT [Id] FROM [dbo].[GraphNode] WHERE [SourceDocumentId] = @SourceDocumentId
            );

            DELETE FROM [dbo].[GraphNode]
            WHERE [SourceDocumentId] = @SourceDocumentId;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            using var transaction = connection.BeginTransaction();
            try
            {
                await connection.ExecuteAsync(new CommandDefinition(sql,
                    new { SourceDocumentId = sourceDocumentId },
                    transaction,
                    cancellationToken: cancellationToken));
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }

            _logger.LogInformation("Deleted graph nodes and edges for document {SourceDocumentId}",
                sourceDocumentId);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete graph data for document {SourceDocumentId}", sourceDocumentId);
            throw new InvalidOperationException(
                $"Failed to delete graph data for document {sourceDocumentId}", ex);
        }
    }
}
