namespace MotorcycleRAG.Domain.Entities;

/// <summary>
/// Represents a directed relationship (edge) between two graph nodes.
/// Corresponds to the SQL Server AS EDGE table [dbo].[GraphEdge].
/// Supports FR-012 (Graph RAG relationship extraction).
/// </summary>
public class GraphEdge
{
    /// <summary>GUID of the source (from) node in the relationship.</summary>
    public Guid FromNodeId { get; set; }

    /// <summary>GUID of the target (to) node in the relationship.</summary>
    public Guid ToNodeId { get; set; }

    /// <summary>
    /// Semantic relationship label.
    /// Examples: "REQUIRES", "PART_OF", "RELATED_TO", "PRECEDES", "REFERENCES".
    /// </summary>
    public string RelationshipType { get; set; } = string.Empty;

    /// <summary>
    /// Edge weight (0.0–1.0). Higher values indicate stronger/more certain relationships.
    /// Defaults to 1.0 (maximum certainty).
    /// </summary>
    public double Weight { get; set; } = 1.0;

    /// <summary>
    /// Optional textual context describing why this relationship was extracted.
    /// Contains the source sentence/paragraph from the manual where the relationship was found.
    /// </summary>
    public string? Context { get; set; }

    /// <summary>UTC timestamp when this edge was created.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
