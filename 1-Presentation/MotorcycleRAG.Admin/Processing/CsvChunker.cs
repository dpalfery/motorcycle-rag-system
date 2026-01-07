using CsvHelper;
using CsvHelper.Configuration;
using System.Collections.ObjectModel;
using System.Linq;
using System.Globalization;
using System.Text;

namespace MotorcycleRAG.Admin.Processing;

/// <summary>
/// Result of CSV chunking operation
/// </summary>
public class CsvChunkingResult
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
            // Input validation
            if (string.IsNullOrWhiteSpace(filePath))
            {
                result.AddError("File path is required.");
                return result;
            }

            // Canonicalize path to prevent directory traversal attacks
            var canonicalPath = Path.GetFullPath(filePath);

            if (!File.Exists(canonicalPath))
            {
                result.AddError($"File not found: {filePath}");
                return result;
            }

            var extension = Path.GetExtension(canonicalPath).ToLowerInvariant();
            if (extension != ".csv")
            {
                result.AddError("Invalid file type. Only CSV files are supported.");
                return result;
            }

            var fileInfo = new FileInfo(canonicalPath);
            const long maxSizeBytes = 50 * 1024 * 1024; // 50 MB
            if (fileInfo.Length > maxSizeBytes)
            {
                result.AddError($"File too large. Max allowed size is 50MB. Actual size: {fileInfo.Length / (1024 * 1024)}MB");
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
                        result.AddWarning($"Bad data at row {rowNum}: {context.RawRecord}");
                    }
                };

                using var reader = new StreamReader(canonicalPath);
                using var csv = new CsvReader(reader, config);

                // Read header
                csv.Read();
                csv.ReadHeader();
                var headers = csv.HeaderRecord?.ToList() ?? new List<string>();

                result.Metadata.SetColumnNames(headers);
                result.Metadata.ColumnCount = headers.Count;
                result.Metadata.HasHeader = true;

                if (headers.Count == 0)
                {
                    result.AddError("No columns found in CSV file");
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
                        result.AddChunk(CreateChunk(currentChunk, chunkIndex++, chunkStartRow, rowNumber - 1, headers));
                        currentChunk = new List<Dictionary<string, object>>();
                        chunkStartRow = rowNumber;
                    }
                }

                // Add final chunk if there are remaining rows
                if (currentChunk.Count > 0)
                {
                    result.AddChunk(CreateChunk(currentChunk, chunkIndex++, chunkStartRow, rowNumber - 1, headers));
                }

                result.Metadata.TotalRows = rowNumber - 1; // Exclude header
                result.Success = true;
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            result.AddError("Processing was cancelled");
        }
        catch (Exception ex)
        {
            result.AddError($"Error processing CSV: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Creates a searchable text representation of CSV data for a chunk
    /// </summary>
    public string CreateSearchableText(
        IReadOnlyList<Dictionary<string, object>> rows,
        IReadOnlyList<string> headers)
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
    private static object ParseValue(string? value)
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
        var metadata = new Dictionary<string, object>
        {
            ["rowCount"] = rows.Count,
            ["startRow"] = startRow,
            ["endRow"] = endRow,
            ["columnCount"] = headers.Count,
            ["searchableText"] = CreateSearchableText(rows, headers)
        };

        var chunk = new CsvChunk
        {
            ChunkIndex = chunkIndex,
            StartRowNumber = startRow,
            EndRowNumber = endRow
        };

        chunk.SetRows(new List<Dictionary<string, object>>(rows));
        chunk.SetMetadata(metadata);

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
            // Canonicalize path to prevent directory traversal attacks
            var canonicalPath = Path.GetFullPath(filePath);

            if (!File.Exists(canonicalPath))
            {
                result.IsValid = false;
                result.AddError($"File not found: {filePath}");
                return result;
            }

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true
            };

            using var reader = new StreamReader(canonicalPath);
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
                result.AddError($"Missing required columns: {string.Join(", ", missingColumns)}");
            }

            // Check if file has at least one data row
            if (!csv.Read())
            {
                result.IsValid = false;
                result.AddError("CSV file contains no data rows");
            }
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.AddError($"Validation error: {ex.Message}");
        }

        return result;
    }
}

/// <summary>
/// Result of validation operation
/// </summary>
public class ValidationResult
{
    private readonly List<string> _errors = new();
    private readonly List<string> _warnings = new();

    public bool IsValid { get; set; }
    public IReadOnlyList<string> Errors => _errors.AsReadOnly();
    public IReadOnlyList<string> Warnings => _warnings.AsReadOnly();

    // Internal methods for modification
    internal void AddError(string error) => _errors.Add(error);
    internal void AddWarning(string warning) => _warnings.Add(warning);
}


