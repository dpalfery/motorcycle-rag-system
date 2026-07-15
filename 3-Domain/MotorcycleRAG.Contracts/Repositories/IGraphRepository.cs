using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Contracts.Models.DTOs.Graph;

namespace MotorcycleRAG.Contracts.Repositories;

/// <summary>
/// Repository abstraction for SQL Server Graph operations (nodes and edges).
/// Implementations use Dapper with parameterized queries — no string concatenation.
/// Graph tables use SQL Server 2017+ AS NODE / AS EDGE syntax.
/// </summary>
public interface IGraphRepository
{
    /// <summary>Upserts a graph node (insert or update by Id).</summary>
    Task UpsertNodeAsync(GraphNodeDto node, CancellationToken cancellationToken = default);

    /// <summary>Upserts a batch of graph nodes in a single transaction.</summary>
    Task UpsertNodesAsync(IReadOnlyList<GraphNodeDto> nodes, CancellationToken cancellationToken = default);

    /// <summary>Upserts a graph edge between two existing nodes.</summary>
    Task UpsertEdgeAsync(GraphEdgeDto edge, CancellationToken cancellationToken = default);

    /// <summary>Upserts a batch of graph edges in a single transaction.</summary>
    Task UpsertEdgesAsync(IReadOnlyList<GraphEdgeDto> edges, CancellationToken cancellationToken = default);

    /// <summary>Returns all nodes for a given source document.</summary>
    Task<IReadOnlyList<GraphNodeDto>> GetNodesByDocumentAsync(
        Guid sourceDocumentId,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes all nodes and edges for a given source document (used on re-ingestion).</summary>
    Task DeleteByDocumentAsync(Guid sourceDocumentId, CancellationToken cancellationToken = default);

    // -------------------------------------------------------------------------
    // Graph traversal queries — used by GraphQueryAgent tool handlers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Searches graph nodes by name (fuzzy LIKE) with optional type filtering.
    /// </summary>
    Task<IReadOnlyList<GraphNodeDto>> SearchNodesAsync(
        string searchTerm,
        string? typeFilter,
        int maxResults,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all direct neighbours (1-hop) of a node via T-SQL MATCH.
    /// Optionally filters by relationship type.
    /// </summary>
    Task<IReadOnlyList<GraphTraversalResultDto>> GetNeighboursAsync(
        Guid nodeId,
        string? relationshipTypeFilter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Discovers multi-hop paths from a source node using a recursive CTE.
    /// The agent controls <paramref name="maxDepth"/> per-call — no fixed cap.
    /// </summary>
    Task<IReadOnlyList<GraphPathResultDto>> FindPathsAsync(
        Guid sourceNodeId,
        int maxDepth,
        int maxResults,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all relationships of a specific type across the entire graph via T-SQL MATCH.
    /// </summary>
    Task<IReadOnlyList<GraphTraversalResultDto>> GetEdgesByTypeAsync(
        string relationshipType,
        int maxResults,
        CancellationToken cancellationToken = default);
}
