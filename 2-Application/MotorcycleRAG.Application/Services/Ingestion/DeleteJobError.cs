namespace MotorcycleRAG.Application.Services.Ingestion;

/// <summary>
/// Categorizes the rejection reason for an ingestion job deletion request so the
/// presentation layer can map to the correct HTTP status without parsing message text.
/// </summary>
public enum DeleteJobError {
    /// <summary>The target ingestion job does not exist.</summary>
    NotFound,

    /// <summary>The job is still active (processing or indexing) and cannot be deleted.</summary>
    Active,

    /// <summary>The job is already in the <c>Deleting</c> state and a cleanup is in flight.</summary>
    AlreadyDeleting,

    /// <summary>The atomic status transition lost a concurrency race.</summary>
    ConcurrentModification
}
