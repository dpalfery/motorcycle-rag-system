namespace MotorcycleRAG.Admin.Models.Processing;

/// <summary>
/// Metadata extracted from CSV
/// </summary>
internal class CsvMetadata
{
    internal int TotalRows { get; set; }
    internal int ColumnCount { get; set; }
    internal IReadOnlyList<string> ColumnNames { get; private set; } = new List<string>().AsReadOnly();
    internal string Delimiter { get; set; } = ",";
    internal bool HasHeader { get; set; } = true;

    // Internal method for setting column names during initialization
    internal void SetColumnNames(List<string> columnNames)
    {
        ColumnNames = columnNames.AsReadOnly();
    }
}
