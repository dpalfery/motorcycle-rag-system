namespace MotorcycleRAG.Admin.Models.Processing;

/// <summary>
/// Result of CSV chunking operation
/// </summary>
internal class CsvChunkingResult
{
    private readonly List<CsvChunk> _chunks = new();
    private readonly List<string> _errors = new();
    private readonly List<string> _warnings = new();

    public bool Success { get; set; }
    public IReadOnlyList<CsvChunk> Chunks => _chunks.AsReadOnly();
    public CsvMetadata Metadata { get; set; } = new();
    public IReadOnlyList<string> Errors => _errors.AsReadOnly();
    public IReadOnlyList<string> Warnings => _warnings.AsReadOnly();

    // Internal methods for modification
    internal void AddChunk(CsvChunk chunk) => _chunks.Add(chunk);
    internal void AddError(string error) => _errors.Add(error);
    internal void AddWarning(string warning) => _warnings.Add(warning);
}
