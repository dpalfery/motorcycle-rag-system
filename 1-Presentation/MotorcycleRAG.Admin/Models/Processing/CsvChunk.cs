namespace MotorcycleRAG.Admin.Models.Processing;

/// <summary>
/// Represents a single row in a CSV file
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Visibility", Justification = "Internal class members")]
internal class CsvRow : Dictionary<string, object>
{
    internal CsvRow() { }
    internal CsvRow(IDictionary<string, object> dictionary) : base(dictionary) { }
}

/// <summary>
/// A chunk of CSV data
/// </summary>
internal class CsvChunk
{
    internal int ChunkIndex { get; set; }
    internal IReadOnlyList<CsvRow> Rows { get; private set; } = new List<CsvRow>().AsReadOnly();
    internal int StartRowNumber { get; set; }
    internal int EndRowNumber { get; set; }
    internal IReadOnlyDictionary<string, object> Metadata { get; private set; } = new Dictionary<string, object>().AsReadOnly();

    // Internal methods for setting properties
    internal void SetRows(List<CsvRow> rows)
    {
        Rows = rows.AsReadOnly();
    }

    internal void SetMetadata(Dictionary<string, object> metadata)
    {
        Metadata = metadata.AsReadOnly();
    }
}
