using MotorcycleRAG.Contracts.Models.DTOs.Graph;

namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Multi-hop graph path result: source → intermediate → target via two edges.
/// Returned by <c>IGraphRepository.FindPathsAsync</c>.
/// For deeper traversals the recursive CTE flattens each path segment into this shape.
/// </summary>
public record GraphPathResultDto(
    GraphNodeDto SourceNode,
    string FirstRelationship,
    GraphNodeDto IntermediateNode,
    string SecondRelationship,
    GraphNodeDto TargetNode);
