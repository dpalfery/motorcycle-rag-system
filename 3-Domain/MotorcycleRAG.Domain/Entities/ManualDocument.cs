using MotorcycleRAG.Domain.Enums;
using System.Diagnostics.CodeAnalysis;

namespace MotorcycleRAG.Domain.Entities;

public class ManualDocument
{
    public Guid DocumentId { get; init; } = Guid.NewGuid();
    public string SourceFileName { get; init; } = string.Empty;
    public string CanonicalBlobContainer { get; init; } = string.Empty;
    public string CanonicalBlobPath { get; init; } = string.Empty;
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "Canonical blob URIs are stored and serialized as nullable string values across the ingestion boundary.")]
    public string? CanonicalBlobUri { get; init; }
    public string? SourceContentHash { get; init; }
    public string DocumentType { get; init; } = string.Empty;
    public string? Make { get; init; }
    public string? Model { get; init; }
    public int? Year { get; init; }
    public DateTimeOffset UploadedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CanonicalizedAtUtc { get; private set; }
    public DateTimeOffset? LastProcessedAtUtc { get; private set; }
    public ManualDocumentStatus CurrentStatus { get; private set; } = ManualDocumentStatus.Pending;
    public string? CurrentStage { get; private set; }
    public Guid? LastSuccessfulRunId { get; private set; }
    public string? LastFailure { get; private set; }

    public void MarkCanonicalized(DateTimeOffset? atUtc = null)
    {
        EnsureNotTerminal();
        CurrentStatus = ManualDocumentStatus.Canonicalized;
        CanonicalizedAtUtc = atUtc ?? DateTimeOffset.UtcNow;
    }

    public void BeginProcessing(string stage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        if (CurrentStatus is ManualDocumentStatus.Processed or ManualDocumentStatus.Failed)
        {
            throw new InvalidOperationException($"A manual document in {CurrentStatus} state cannot begin processing.");
        }

        CurrentStatus = ManualDocumentStatus.Processing;
        CurrentStage = stage;
    }

    public void SetStage(string stage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        if (CurrentStatus != ManualDocumentStatus.Processing)
        {
            throw new InvalidOperationException("A manual document must be processing before its stage can change.");
        }

        CurrentStage = stage;
    }

    public void MarkProcessed(Guid runId, DateTimeOffset? atUtc = null)
    {
        if (runId == Guid.Empty)
        {
            throw new ArgumentException("A successful processing run is required.", nameof(runId));
        }

        CurrentStatus = ManualDocumentStatus.Processed;
        LastSuccessfulRunId = runId;
        LastProcessedAtUtc = atUtc ?? DateTimeOffset.UtcNow;
        LastFailure = null;
    }

    public void MarkFailed(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        EnsureNotTerminal();
        CurrentStatus = ManualDocumentStatus.Failed;
        LastFailure = reason;
    }

    private void EnsureNotTerminal()
    {
        if (CurrentStatus is ManualDocumentStatus.Processed or ManualDocumentStatus.Failed)
        {
            throw new InvalidOperationException($"A manual document in {CurrentStatus} state cannot be canonicalized.");
        }
    }
}
