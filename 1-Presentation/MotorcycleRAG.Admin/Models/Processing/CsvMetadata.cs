namespace MotorcycleRAG.Admin.Models.Processing;

/// <summary>
/// Metadata extracted from CSV
/// </summary>
public class CsvMetadata
{
    public int TotalRows { get; set; }
    public int ColumnCount { get; set; }
    public IReadOnlyList<string> ColumnNames { get; private set; } = new List<string>().AsReadOnly();
    public string Delimiter { get; set; } = ",";
    public bool HasHeader { get; set; } = true;

    // Internal method for setting column names during initialization
    internal void SetColumnNames(List<string> columnNames)
    {
        ColumnNames = columnNames.AsReadOnly();
    }
}
