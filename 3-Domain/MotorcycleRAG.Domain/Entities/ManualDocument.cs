using MotorcycleRAG.Domain.Enums;
using System.Diagnostics.CodeAnalysis;

namespace MotorcycleRAG.Domain.Entities;

public class ManualDocument
{
    public Guid DocumentId { get; }
    public string SourceFileName { get; }
    public string CanonicalBlobContainer { get; }
    public string CanonicalBlobPath { get; }
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "Canonical blob URIs are stored and serialized as nullable string values across the ingestion boundary.")]
    public string? CanonicalBlobUri { get; }
    public string? SourceContentHash { get; }
    public string DocumentType { get; }
    public string? Make { get; }
    public string? Model { get; }
    public int? Year { get; }
    public DateTimeOffset UploadedAtUtc { get; }
    public DateTimeOffset? CanonicalizedAtUtc { get; private set; }
    public DateTimeOffset? LastProcessedAtUtc { get; private set; }
    public ManualDocumentStatus CurrentStatus { get; private set; }
    public string? CurrentStage { get; private set; }
    public Guid? LastSuccessfulRunId { get; private set; }
    public string? LastFailure { get; private set; }

    private ManualDocument(
        Guid documentId,
        string sourceFileName,
        string canonicalBlobContainer,
        string canonicalBlobPath,
        string? canonicalBlobUri,
        string? sourceContentHash,
        string documentType,
        string? make,
        string? model,
        int? year,
        DateTimeOffset uploadedAtUtc,
        DateTimeOffset? canonicalizedAtUtc,
        DateTimeOffset? lastProcessedAtUtc,
        ManualDocumentStatus currentStatus,
        string? currentStage,
        Guid? lastSuccessfulRunId,
        string? lastFailure)
    {
        DocumentId = documentId;
        SourceFileName = sourceFileName;
        CanonicalBlobContainer = canonicalBlobContainer;
        CanonicalBlobPath = canonicalBlobPath;
        CanonicalBlobUri = canonicalBlobUri;
        SourceContentHash = sourceContentHash;
        DocumentType = documentType;
        Make = make;
        Model = model;
        Year = year;
        UploadedAtUtc = uploadedAtUtc;
        CanonicalizedAtUtc = canonicalizedAtUtc;
        LastProcessedAtUtc = lastProcessedAtUtc;
        CurrentStatus = currentStatus;
        CurrentStage = currentStage;
        LastSuccessfulRunId = lastSuccessfulRunId;
        LastFailure = lastFailure;
    }

    /// <summary>
    /// Creates a newly-uploaded manual document in the <see cref="ManualDocumentStatus.Pending"/> state.
    /// </summary>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "Canonical blob URIs are stored and serialized as nullable string values across the ingestion boundary.")]
    public static ManualDocument Create(
        Guid documentId,
        string sourceFileName,
        string canonicalBlobContainer,
        string canonicalBlobPath,
        string documentType,
        string? canonicalBlobUri = null,
        string? sourceContentHash = null,
        string? make = null,
        string? model = null,
        int? year = null,
        DateTimeOffset? uploadedAtUtc = null) =>
        Rehydrate(
            documentId,
            sourceFileName,
            canonicalBlobContainer,
            canonicalBlobPath,
            canonicalBlobUri,
            sourceContentHash,
            documentType,
            make,
            model,
            year,
            uploadedAtUtc ?? DateTimeOffset.UtcNow,
            canonicalizedAtUtc: null,
            lastProcessedAtUtc: null,
            currentStatus: ManualDocumentStatus.Pending,
            currentStage: null,
            lastSuccessfulRunId: null,
            lastFailure: null);

    /// <summary>
    /// Rehydrates a persisted manual document after validating the complete lifecycle state at the
    /// Persistence boundary. Invalid database rows are rejected rather than becoming a partially-valid
    /// domain entity.
    /// </summary>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "Canonical blob URIs are stored and serialized as nullable string values across the ingestion boundary.")]
    public static ManualDocument Rehydrate(
        Guid documentId,
        string sourceFileName,
        string canonicalBlobContainer,
        string canonicalBlobPath,
        string? canonicalBlobUri,
        string? sourceContentHash,
        string documentType,
        string? make,
        string? model,
        int? year,
        DateTimeOffset uploadedAtUtc,
        DateTimeOffset? canonicalizedAtUtc,
        DateTimeOffset? lastProcessedAtUtc,
        ManualDocumentStatus currentStatus,
        string? currentStage,
        Guid? lastSuccessfulRunId,
        string? lastFailure)
    {
        if (documentId == Guid.Empty)
        {
            throw new ArgumentException("Document id is required.", nameof(documentId));
        }

        if (!Enum.IsDefined(currentStatus))
        {
            throw new ArgumentOutOfRangeException(nameof(currentStatus), currentStatus, "Unknown manual document status.");
        }

        return new ManualDocument(
            documentId,
            sourceFileName,
            canonicalBlobContainer,
            canonicalBlobPath,
            canonicalBlobUri,
            sourceContentHash,
            documentType,
            make,
            model,
            year,
            uploadedAtUtc.ToUniversalTime(),
            canonicalizedAtUtc?.ToUniversalTime(),
            lastProcessedAtUtc?.ToUniversalTime(),
            currentStatus,
            currentStage,
            lastSuccessfulRunId,
            lastFailure);
    }

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
