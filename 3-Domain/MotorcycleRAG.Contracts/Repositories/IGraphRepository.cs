using MotorcycleRAG.Domain.Entities;

namespace MotorcycleRAG.Contracts.Repositories;

/// <summary>
/// Repository abstraction for SQL Server Graph operations (nodes and edges).
/// Implementations use Dapper with parameterized queries — no string concatenation.
/// Graph tables use SQL Server 2017+ AS NODE / AS EDGE syntax.
/// </summary>
public interface IGraphRepository
{
    /// <summary>Upserts a graph node (insert or update by Id).</summary>
    Task UpsertNodeAsync(GraphNode node, CancellationToken cancellationToken = default);

    /// <summary>Upserts a batch of graph nodes in a single transaction.</summary>
    Task UpsertNodesAsync(IReadOnlyList<GraphNode> nodes, CancellationToken cancellationToken = default);

    /// <summary>Upserts a graph edge between two existing nodes.</summary>
    Task UpsertEdgeAsync(GraphEdge edge, CancellationToken cancellationToken = default);

    /// <summary>Upserts a batch of graph edges in a single transaction.</summary>
    Task UpsertEdgesAsync(IReadOnlyList<GraphEdge> edges, CancellationToken cancellationToken = default);

    /// <summary>Returns all nodes for a given source document.</summary>
    Task<IReadOnlyList<GraphNode>> GetNodesByDocumentAsync(
        Guid sourceDocumentId,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes all nodes and edges for a given source document (used on re-ingestion).</summary>
    Task DeleteByDocumentAsync(Guid sourceDocumentId, CancellationToken cancellationToken = default);
}
