using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using System.Text;

namespace MotorcycleRAG.Admin.Processing;

/// <summary>
/// Result of CSV chunking operation
/// </summary>
public class CsvChunkingResult
{
    public bool Success { get; set; }
    public List<CsvChunk> Chunks { get; set; } = new();
    public CsvMetadata Metadata { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Metadata extracted from CSV
/// </summary>
public class CsvMetadata
{
    public int TotalRows { get; set; }
    public int ColumnCount { get; set; }
    public List<string> ColumnNames { get; set; } = new();
    public string Delimiter { get; set; } = ",";
    public bool HasHeader { get; set; } = true;
}

/// <summary>
/// A chunk of CSV data
/// </summary>
public class CsvChunk
{
    public int ChunkIndex { get; set; }
    public List<Dictionary<string, object>> Rows { get; set; } = new();
    public int StartRowNumber { get; set; }
    public int EndRowNumber { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Service for chunking CSV files for motorcycle specification data
/// </summary>
public class CsvChunker
{
    private readonly int _rowsPerChunk;

    public CsvChunker(int rowsPerChunk = 100)
    {
        if (rowsPerChunk <= 0)
            throw new ArgumentException("Rows per chunk must be positive", nameof(rowsPerChunk));

        _rowsPerChunk = rowsPerChunk;
    }

    /// <summary>
    /// Processes a CSV file and extracts chunks
    /// </summary>
    public async Task<CsvChunkingResult> ProcessCsvAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var result = new CsvChunkingResult();

        try
        {
            if (!File.Exists(filePath))
            {
                result.Errors.Add($"File not found: {filePath}");
                return result;
            }

            await Task.Run(() =>
            {
                var config = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = true,
                    TrimOptions = TrimOptions.Trim,
                    BadDataFound = context =>
                    {
                        var rowNum = context.Context?.Parser?.Row ?? 0;
                        result.Warnings.Add($"Bad data at row {rowNum}: {context.RawRecord}");
                    }
                };

                using var reader = new StreamReader(filePath);
                using var csv = new CsvReader(reader, config);

                // Read header
                csv.Read();
                csv.ReadHeader();
                var headers = csv.HeaderRecord?.ToList() ?? new List<string>();
                
                result.Metadata.ColumnNames = headers;
                result.Metadata.ColumnCount = headers.Count;
                result.Metadata.HasHeader = true;

                if (headers.Count == 0)
                {
                    result.Errors.Add("No columns found in CSV file");
                    return;
                }

                // Process rows in chunks
                var currentChunk = new List<Dictionary<string, object>>();
                var chunkIndex = 0;
                var rowNumber = 1; // Starting from 1 (after header)
                var chunkStartRow = rowNumber;

                while (csv.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var row = new Dictionary<string, object>();
                    
                    for (int i = 0; i < headers.Count; i++)
                    {
                        var columnName = headers[i];
                        var value = csv.GetField(i);
                        
                        // Try to parse as appropriate type
                        row[columnName] = ParseValue(value);
                    }

                    currentChunk.Add(row);
                    rowNumber++;

                    // Create chunk when we reach the target size
                    if (currentChunk.Count >= _rowsPerChunk)
                    {
                        result.Chunks.Add(CreateChunk(currentChunk, chunkIndex++, chunkStartRow, rowNumber - 1, headers));
                        currentChunk = new List<Dictionary<string, object>>();
                        chunkStartRow = rowNumber;
                    }
                }

                // Add final chunk if there are remaining rows
                if (currentChunk.Count > 0)
                {
                    result.Chunks.Add(CreateChunk(currentChunk, chunkIndex++, chunkStartRow, rowNumber - 1, headers));
                }

                result.Metadata.TotalRows = rowNumber - 1; // Exclude header
                result.Success = true;
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            result.Errors.Add("Processing was cancelled");
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Error processing CSV: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Creates a searchable text representation of CSV data for a chunk
    /// </summary>
    public string CreateSearchableText(List<Dictionary<string, object>> rows, List<string> headers)
    {
        var sb = new StringBuilder();
        
        foreach (var row in rows)
        {
            // Create a natural language representation of each row
            var parts = new List<string>();
            
            foreach (var header in headers)
            {
                if (row.TryGetValue(header, out var value) && value != null)
                {
                    var valueStr = value.ToString();
                    if (!string.IsNullOrWhiteSpace(valueStr))
                    {
                        parts.Add($"{header}: {valueStr}");
                    }
                }
            }
            
            if (parts.Count > 0)
            {
                sb.AppendLine(string.Join(", ", parts));
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Parses a string value to an appropriate type
    /// </summary>
    private object ParseValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        // Try to parse as number
        if (int.TryParse(value, out var intValue))
            return intValue;

        if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var doubleValue))
            return doubleValue;

        // Try to parse as boolean
        if (bool.TryParse(value, out var boolValue))
            return boolValue;

        // Try to parse as date
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateValue))
            return dateValue;

        // Return as string
        return value;
    }

    /// <summary>
    /// Creates a CsvChunk from row data
    /// </summary>
    private CsvChunk CreateChunk(
        List<Dictionary<string, object>> rows,
        int chunkIndex,
        int startRow,
        int endRow,
        List<string> headers)
    {
        var chunk = new CsvChunk
        {
            ChunkIndex = chunkIndex,
            Rows = new List<Dictionary<string, object>>(rows),
            StartRowNumber = startRow,
            EndRowNumber = endRow,
            Metadata = new Dictionary<string, object>
            {
                ["rowCount"] = rows.Count,
                ["startRow"] = startRow,
                ["endRow"] = endRow,
                ["columnCount"] = headers.Count,
                ["searchableText"] = CreateSearchableText(rows, headers)
            }
        };

        return chunk;
    }

    /// <summary>
    /// Validates CSV structure for motorcycle specifications
    /// </summary>
    public ValidationResult ValidateMotorcycleSpecCsv(string filePath)
    {
        var result = new ValidationResult { IsValid = true };

        try
        {
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true
            };

            using var reader = new StreamReader(filePath);
            using var csv = new CsvReader(reader, config);

            csv.Read();
            csv.ReadHeader();
            var headers = csv.HeaderRecord?.ToList() ?? new List<string>();

            // Check for common motorcycle specification columns
            var expectedColumns = new[] { "make", "model", "year" };
            var missingColumns = expectedColumns.Where(col => 
                !headers.Any(h => h.Equals(col, StringComparison.OrdinalIgnoreCase))).ToList();

            if (missingColumns.Any())
            {
                result.IsValid = false;
                result.Errors.Add($"Missing required columns: {string.Join(", ", missingColumns)}");
            }

            // Check if file has at least one data row
            if (!csv.Read())
            {
                result.IsValid = false;
                result.Errors.Add("CSV file contains no data rows");
            }
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.Errors.Add($"Validation error: {ex.Message}");
        }

        return result;
    }
}

/// <summary>
/// Result of validation operation
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
