using MotorcycleRAG.Contracts.Models.DTOs.Graph;

namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Single-hop graph traversal result: a source node connected to a target node via one edge.
/// Returned by <c>IGraphRepository.GetNeighboursAsync</c> and <c>GetEdgesByTypeAsync</c>.
/// </summary>
public record GraphTraversalResultDto(
    GraphNodeDto FromNode,
    string RelationshipType,
    double Weight,
    string? Context,
    GraphNodeDto ToNode);
