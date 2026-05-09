using MotorcycleRAG.Domain.Entities;

namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Multi-hop graph path result: source → intermediate → target via two edges.
/// Returned by <c>IGraphRepository.FindPathsAsync</c>.
/// For deeper traversals the recursive CTE flattens each path segment into this shape.
/// </summary>
public record GraphPathResult(
    GraphNode SourceNode,
    string FirstRelationship,
    GraphNode IntermediateNode,
    string SecondRelationship,
    GraphNode TargetNode);
