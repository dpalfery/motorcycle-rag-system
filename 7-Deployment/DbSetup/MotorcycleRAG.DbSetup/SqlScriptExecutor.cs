using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.RegularExpressions;

namespace MotorcycleRAG.DbSetup;

public class SqlScriptExecutor
{
    private readonly ILogger<SqlScriptExecutor> _logger;
    private readonly IDbSetupConnectionFactory _connectionFactory;

    public SqlScriptExecutor(ILogger<SqlScriptExecutor> logger, IDbSetupConnectionFactory connectionFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
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

            await using var connection = _connectionFactory.Create(connectionString);
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

                    await using var command = connection.CreateCommand();
                    command.CommandText = batch;
                    command.CommandTimeout = 300; // 5 minutes

                    await command.ExecuteNonQueryAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to execute batch {BatchNumber}", batchNumber);
                    _logger.LogError("Batch content: {BatchContent}", batch.Length > 200 ? batch.Substring(0, 200) + "..." : batch);
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
