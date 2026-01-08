namespace MotorcycleRAG.Admin.Models.Processing;

/// <summary>
/// A chunk of CSV data
/// </summary>
internal class CsvChunk
{
    internal int ChunkIndex { get; set; }
    internal IReadOnlyList<Dictionary<string, object>> Rows { get; private set; } = new List<Dictionary<string, object>>().AsReadOnly();
    internal int StartRowNumber { get; set; }
    internal int EndRowNumber { get; set; }
    internal IReadOnlyDictionary<string, object> Metadata { get; private set; } = new Dictionary<string, object>().AsReadOnly();

    // Internal methods for setting properties
    internal void SetRows(List<Dictionary<string, object>> rows)
    {
        Rows = rows.AsReadOnly();
    }

    internal void SetMetadata(Dictionary<string, object> metadata)
    {
        Metadata = metadata.AsReadOnly();
    }
}
