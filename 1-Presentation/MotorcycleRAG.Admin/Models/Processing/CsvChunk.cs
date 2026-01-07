namespace MotorcycleRAG.Admin.Models.Processing;

/// <summary>
/// A chunk of CSV data
/// </summary>
public class CsvChunk
{
    public int ChunkIndex { get; set; }
    public IReadOnlyList<Dictionary<string, object>> Rows { get; private set; } = new List<Dictionary<string, object>>().AsReadOnly();
    public int StartRowNumber { get; set; }
    public int EndRowNumber { get; set; }
    public IReadOnlyDictionary<string, object> Metadata { get; private set; } = new Dictionary<string, object>().AsReadOnly();

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
