namespace MotorcycleRAG.Contracts.Models.DTOs.Graph;

/// <summary>
/// Data transfer shape for an entity node in the SQL Server Graph database.
/// Corresponds to the SQL Server AS NODE table [dbo].[GraphNode].
/// Supports FR-012 (Graph RAG relationship extraction).
/// </summary>
public sealed class GraphNodeDto
{
    /// <summary>Primary key — GUID, maps to [Id] in the AS NODE table.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Display name of the entity (e.g., "Oil Change", "Honda CBR1000RR", "Valve Clearance").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Entity type tag for graph traversal filtering.
    /// Examples: "Procedure", "Component", "Motorcycle", "Specification", "Warning".
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Optional textual description of the entity extracted from the manual.</summary>
    public string? Description { get; set; }

    /// <summary>GUID of the canonical <c>ManualDocument</c> this node was extracted from.</summary>
    public Guid? SourceDocumentId { get; set; }

    /// <summary>UTC timestamp when this node was first created.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>UTC timestamp of last update (upsert). Null if never updated after creation.</summary>
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
