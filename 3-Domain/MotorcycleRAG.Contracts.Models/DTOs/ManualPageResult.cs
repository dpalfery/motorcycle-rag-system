namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Encapsulates the result of a manual page retrieval, including the content stream and optional ETag.
/// </summary>
/// <param name="Content">The image content stream.</param>
/// <param name="ETag">The blob ETag for HTTP conditional caching; null when unavailable.</param>
public sealed record ManualPageResult(Stream Content, string? ETag);
