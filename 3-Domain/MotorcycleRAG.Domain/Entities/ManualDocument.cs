using MotorcycleRAG.Domain.Enums;
using System.Diagnostics.CodeAnalysis;

namespace MotorcycleRAG.Domain.Entities;

public class ManualDocument
{
    public Guid DocumentId { get; set; } = Guid.NewGuid();
    public string SourceFileName { get; set; } = string.Empty;
    public string CanonicalBlobContainer { get; set; } = string.Empty;
    public string CanonicalBlobPath { get; set; } = string.Empty;
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "Canonical blob URIs are stored and serialized as nullable string values across the ingestion boundary.")]
    public string? CanonicalBlobUri { get; set; }
    public string? SourceContentHash { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string? Make { get; set; }
    public string? Model { get; set; }
    public int? Year { get; set; }
    public DateTimeOffset UploadedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CanonicalizedAtUtc { get; set; }
    public DateTimeOffset? LastProcessedAtUtc { get; set; }
    public ManualDocumentStatus CurrentStatus { get; set; } = ManualDocumentStatus.Pending;
    public string? CurrentStage { get; set; }
    public Guid? LastSuccessfulRunId { get; set; }
    public string? LastFailure { get; set; }
}
