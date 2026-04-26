using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.RegularExpressions;

namespace MotorcycleRAG.DbSetup;

public class SqlScriptExecutor
{
    private readonly ILogger<SqlScriptExecutor> _logger;

    public SqlScriptExecutor(ILogger<SqlScriptExecutor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> ExecuteScriptFileAsync(string connectionString, string scriptFilePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(scriptFilePath))
        {
            _logger.LogError("Script file not found: {ScriptFilePath}", scriptFilePath);
            return false;
        }

        _logger.LogInformation("Executing SQL script: {ScriptFilePath}", scriptFilePath);

        try
        {
            var scriptContent = await File.ReadAllTextAsync(scriptFilePath, cancellationToken);
            var batches = SplitScriptIntoBatches(scriptContent);

            _logger.LogInformation("Script contains {BatchCount} batches", batches.Count);

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            int batchNumber = 0;
            foreach (var batch in batches)
            {
                batchNumber++;
                
                if (string.IsNullOrWhiteSpace(batch))
                {
                    continue;
                }

                try
                {
                    _logger.LogDebug("Executing batch {BatchNumber}/{TotalBatches}", batchNumber, batches.Count);
                    
                    await using var command = new SqlCommand(batch, connection)
                    {
                        CommandTimeout = 300 // 5 minutes
                    };
                    
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }
                catch (SqlException ex)
                {
                    _logger.LogError("Failed to execute batch {BatchNumber}. ExceptionType={ExceptionType}", batchNumber, ex.GetType().Name);
                    _logger.LogError("Failed batch {BatchNumber} length: {BatchLength}", batchNumber, batch.Length);
                    return false;
                }
            }

            _logger.LogInformation("Successfully executed script: {ScriptFilePath}", scriptFilePath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to execute script: {ScriptFilePath}. ExceptionType={ExceptionType}", scriptFilePath, ex.GetType().Name);
            return false;
        }
    }

    private List<string> SplitScriptIntoBatches(string scriptContent)
    {
        // Split by GO statements (case-insensitive, must be on its own line)
        // This regex matches GO that is on its own line, possibly with whitespace
        var goRegex = new Regex(@"^\s*GO\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        
        var batches = goRegex.Split(scriptContent);
        
        // Filter out empty batches and trim whitespace
        var result = batches
            .Select(b => b.Trim())
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .ToList();

        return result;
    }
}
