namespace MotorcycleRAG.Application.Services.Ingestion;

/// <summary>
/// Thrown by <see cref="IIngestionJobService.DeleteJobAsync"/> when a deletion request is
/// rejected. Carries a structured <see cref="Error"/> so the presentation layer can map to
/// the correct HTTP status code deterministically, rather than parsing the message text.
/// </summary>
/// <remarks>
/// Derives from <see cref="InvalidOperationException"/> so that callers still catching the
/// base type continue to function as a backward-compatibility safety net.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1032:Implement standard exception constructors",
    Justification = "The Error category is a mandatory part of this exception's contract; a parameterless or message-only constructor would leave Error defaulting to a misleading NotFound. Serialization is inherited from InvalidOperationException.")]
public sealed class DeleteJobException : InvalidOperationException {
    /// <summary>
    /// Gets the categorized rejection reason.
    /// </summary>
    public DeleteJobError Error { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DeleteJobException"/> class.
    /// </summary>
    /// <param name="error">The categorized rejection reason.</param>
    /// <param name="message">The human-readable detail message describing why deletion was rejected.</param>
    public DeleteJobException(DeleteJobError error, string message)
        : base(message) {
        Error = error;
    }
}
