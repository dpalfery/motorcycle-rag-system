using Dapper;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Models.DTOs;
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

    // -------------------------------------------------------------------------
    // Graph traversal queries — used by GraphQueryAgent tool handlers
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<IReadOnlyList<GraphNode>> SearchNodesAsync(
        string searchTerm, string? typeFilter, int maxResults,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(searchTerm);
        if (maxResults <= 0) maxResults = 20;

        var hasTypeFilter = !string.IsNullOrWhiteSpace(typeFilter);
        var sql = hasTypeFilter
            ? @"SELECT TOP(@MaxResults) [Id], [Name], [Type], [Description], [SourceDocumentId], [CreatedAtUtc], [UpdatedAtUtc]
                FROM [dbo].[GraphNode]
                WHERE [Name] LIKE @Pattern AND [Type] = @TypeFilter
                ORDER BY [Name];"
            : @"SELECT TOP(@MaxResults) [Id], [Name], [Type], [Description], [SourceDocumentId], [CreatedAtUtc], [UpdatedAtUtc]
                FROM [dbo].[GraphNode]
                WHERE [Name] LIKE @Pattern
                ORDER BY [Name];";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var results = await connection.QueryAsync<GraphNode>(
                new CommandDefinition(sql, new
                {
                    MaxResults = maxResults,
                    Pattern = $"%{searchTerm}%",
                    TypeFilter = typeFilter
                }, cancellationToken: cancellationToken));

            var list = results.ToList().AsReadOnly();
            _logger.LogInformation("SearchNodesAsync: term={SearchTerm} type={TypeFilter} found={Count}",
                searchTerm, typeFilter ?? "any", list.Count);
            return list;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search graph nodes for term {SearchTerm}", searchTerm);
            throw new InvalidOperationException($"Failed to search graph nodes for term '{searchTerm}'", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<GraphTraversalResult>> GetNeighboursAsync(
        Guid nodeId, string? relationshipTypeFilter,
        CancellationToken cancellationToken = default)
    {
        var hasRelFilter = !string.IsNullOrWhiteSpace(relationshipTypeFilter);
        var sql = hasRelFilter
            ? @"SELECT
                    n1.[Id] AS FromId, n1.[Name] AS FromName, n1.[Type] AS FromType, n1.[Description] AS FromDescription, n1.[SourceDocumentId] AS FromSourceDocumentId, n1.[CreatedAtUtc] AS FromCreatedAtUtc, n1.[UpdatedAtUtc] AS FromUpdatedAtUtc,
                    e.[RelationshipType], e.[Weight], e.[Context],
                    n2.[Id] AS ToId, n2.[Name] AS ToName, n2.[Type] AS ToType, n2.[Description] AS ToDescription, n2.[SourceDocumentId] AS ToSourceDocumentId, n2.[CreatedAtUtc] AS ToCreatedAtUtc, n2.[UpdatedAtUtc] AS ToUpdatedAtUtc
                FROM [dbo].[GraphNode] AS n1, [dbo].[GraphEdge] AS e, [dbo].[GraphNode] AS n2
                WHERE MATCH(n1-(e)->n2)
                  AND n1.[Id] = @NodeId
                  AND e.[RelationshipType] = @RelFilter;"
            : @"SELECT
                    n1.[Id] AS FromId, n1.[Name] AS FromName, n1.[Type] AS FromType, n1.[Description] AS FromDescription, n1.[SourceDocumentId] AS FromSourceDocumentId, n1.[CreatedAtUtc] AS FromCreatedAtUtc, n1.[UpdatedAtUtc] AS FromUpdatedAtUtc,
                    e.[RelationshipType], e.[Weight], e.[Context],
                    n2.[Id] AS ToId, n2.[Name] AS ToName, n2.[Type] AS ToType, n2.[Description] AS ToDescription, n2.[SourceDocumentId] AS ToSourceDocumentId, n2.[CreatedAtUtc] AS ToCreatedAtUtc, n2.[UpdatedAtUtc] AS ToUpdatedAtUtc
                FROM [dbo].[GraphNode] AS n1, [dbo].[GraphEdge] AS e, [dbo].[GraphNode] AS n2
                WHERE MATCH(n1-(e)->n2)
                  AND n1.[Id] = @NodeId;";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var rows = await connection.QueryAsync(
                new CommandDefinition(sql, new { NodeId = nodeId, RelFilter = relationshipTypeFilter },
                    cancellationToken: cancellationToken));

            var results = rows.Select(MapTraversalRow).ToList().AsReadOnly();
            _logger.LogInformation("GetNeighboursAsync: nodeId={NodeId} filter={Filter} found={Count}",
                nodeId, relationshipTypeFilter ?? "any", results.Count);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get neighbours for node {NodeId}", nodeId);
            throw new InvalidOperationException($"Failed to get neighbours for node {nodeId}", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<GraphPathResult>> FindPathsAsync(
        Guid sourceNodeId, int maxDepth, int maxResults,
        CancellationToken cancellationToken = default)
    {
        if (maxDepth <= 0) maxDepth = 2;
        if (maxResults <= 0) maxResults = 50;

        // Recursive CTE for variable-depth traversal via explicit FromNodeId/ToNodeId columns
        const string sql = @"
            WITH Paths AS (
                -- Anchor: direct neighbours (depth 1)
                SELECT
                    n1.[Id]   AS SourceId, n1.[Name] AS SourceName, n1.[Type] AS SourceType, n1.[Description] AS SourceDescription,
                    e.[RelationshipType] AS FirstRelationship,
                    n2.[Id]   AS IntermediateId, n2.[Name] AS IntermediateName, n2.[Type] AS IntermediateType, n2.[Description] AS IntermediateDescription,
                    CAST('' AS NVARCHAR(200)) AS SecondRelationship,
                    n2.[Id]   AS TargetId, n2.[Name] AS TargetName, n2.[Type] AS TargetType, n2.[Description] AS TargetDescription,
                    1 AS Depth
                FROM [dbo].[GraphNode] AS n1
                INNER JOIN [dbo].[GraphEdge] AS e ON e.[FromNodeId] = n1.[Id]
                INNER JOIN [dbo].[GraphNode] AS n2 ON n2.[Id] = e.[ToNodeId]
                WHERE n1.[Id] = @SourceNodeId

                UNION ALL

                -- Recursive: extend by one hop
                SELECT
                    p.SourceId, p.SourceName, p.SourceType, p.SourceDescription,
                    p.FirstRelationship,
                    p.TargetId AS IntermediateId, p.TargetName AS IntermediateName, p.TargetType AS IntermediateType, p.TargetDescription AS IntermediateDescription,
                    e2.[RelationshipType] AS SecondRelationship,
                    n3.[Id]   AS TargetId, n3.[Name] AS TargetName, n3.[Type] AS TargetType, n3.[Description] AS TargetDescription,
                    p.Depth + 1
                FROM Paths p
                INNER JOIN [dbo].[GraphEdge] AS e2 ON e2.[FromNodeId] = p.TargetId
                INNER JOIN [dbo].[GraphNode] AS n3 ON n3.[Id] = e2.[ToNodeId]
                WHERE p.Depth < @MaxDepth
                  AND n3.[Id] <> @SourceNodeId  -- avoid cycles back to source
            )
            SELECT TOP(@MaxResults) *
            FROM Paths
            WHERE Depth >= 2
            ORDER BY Depth, SourceName
            OPTION (MAXRECURSION 100);";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var rows = await connection.QueryAsync(
                new CommandDefinition(sql, new { SourceNodeId = sourceNodeId, MaxDepth = maxDepth, MaxResults = maxResults },
                    cancellationToken: cancellationToken));

            var results = rows.Select(r => new GraphPathResult(
                new GraphNode { Id = (Guid)r.SourceId, Name = (string)r.SourceName, Type = (string)r.SourceType, Description = (string?)r.SourceDescription },
                (string)r.FirstRelationship,
                new GraphNode { Id = (Guid)r.IntermediateId, Name = (string)r.IntermediateName, Type = (string)r.IntermediateType, Description = (string?)r.IntermediateDescription },
                (string)r.SecondRelationship,
                new GraphNode { Id = (Guid)r.TargetId, Name = (string)r.TargetName, Type = (string)r.TargetType, Description = (string?)r.TargetDescription }
            )).ToList().AsReadOnly();

            _logger.LogInformation("FindPathsAsync: source={SourceNodeId} maxDepth={MaxDepth} found={Count}",
                sourceNodeId, maxDepth, results.Count);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to find paths from node {SourceNodeId}", sourceNodeId);
            throw new InvalidOperationException($"Failed to find paths from node {sourceNodeId}", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<GraphTraversalResult>> GetEdgesByTypeAsync(
        string relationshipType, int maxResults,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relationshipType);
        if (maxResults <= 0) maxResults = 50;

        const string sql = @"
            SELECT TOP(@MaxResults)
                n1.[Id] AS FromId, n1.[Name] AS FromName, n1.[Type] AS FromType, n1.[Description] AS FromDescription, n1.[SourceDocumentId] AS FromSourceDocumentId, n1.[CreatedAtUtc] AS FromCreatedAtUtc, n1.[UpdatedAtUtc] AS FromUpdatedAtUtc,
                e.[RelationshipType], e.[Weight], e.[Context],
                n2.[Id] AS ToId, n2.[Name] AS ToName, n2.[Type] AS ToType, n2.[Description] AS ToDescription, n2.[SourceDocumentId] AS ToSourceDocumentId, n2.[CreatedAtUtc] AS ToCreatedAtUtc, n2.[UpdatedAtUtc] AS ToUpdatedAtUtc
            FROM [dbo].[GraphNode] AS n1, [dbo].[GraphEdge] AS e, [dbo].[GraphNode] AS n2
            WHERE MATCH(n1-(e)->n2)
              AND e.[RelationshipType] = @RelType;";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var rows = await connection.QueryAsync(
                new CommandDefinition(sql, new { RelType = relationshipType, MaxResults = maxResults },
                    cancellationToken: cancellationToken));

            var results = rows.Select(MapTraversalRow).ToList().AsReadOnly();
            _logger.LogInformation("GetEdgesByTypeAsync: type={RelType} found={Count}",
                relationshipType, results.Count);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get edges by type {RelType}", relationshipType);
            throw new InvalidOperationException($"Failed to get edges by type '{relationshipType}'", ex);
        }
    }

    // -------------------------------------------------------------------------
    // Mapping helpers
    // -------------------------------------------------------------------------

    private static GraphTraversalResult MapTraversalRow(dynamic r) => new(
        new GraphNode
        {
            Id = (Guid)r.FromId,
            Name = (string)r.FromName,
            Type = (string)r.FromType,
            Description = (string?)r.FromDescription,
            SourceDocumentId = (Guid?)r.FromSourceDocumentId,
            CreatedAtUtc = (DateTimeOffset)r.FromCreatedAtUtc,
            UpdatedAtUtc = (DateTimeOffset?)r.FromUpdatedAtUtc
        },
        (string)r.RelationshipType,
        (double)r.Weight,
        (string?)r.Context,
        new GraphNode
        {
            Id = (Guid)r.ToId,
            Name = (string)r.ToName,
            Type = (string)r.ToType,
            Description = (string?)r.ToDescription,
            SourceDocumentId = (Guid?)r.ToSourceDocumentId,
            CreatedAtUtc = (DateTimeOffset)r.ToCreatedAtUtc,
            UpdatedAtUtc = (DateTimeOffset?)r.ToUpdatedAtUtc
        });
}
