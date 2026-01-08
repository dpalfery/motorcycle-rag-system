namespace MotorcycleRAG.Admin.Models.Processing;

/// <summary>
/// Result of CSV chunking operation
/// </summary>
internal class CsvChunkingResult
{
    private readonly List<CsvChunk> _chunks = new();
    private readonly List<string> _errors = new();
    private readonly List<string> _warnings = new();

    internal bool Success { get; set; }
    internal IReadOnlyList<CsvChunk> Chunks => _chunks.AsReadOnly();
    internal CsvMetadata Metadata { get; set; } = new();
    internal IReadOnlyList<string> Errors => _errors.AsReadOnly();
    internal IReadOnlyList<string> Warnings => _warnings.AsReadOnly();

    // Internal methods for modification
    internal void AddChunk(CsvChunk chunk) => _chunks.Add(chunk);
    internal void AddError(string error) => _errors.Add(error);
    internal void AddWarning(string warning) => _warnings.Add(warning);
}
