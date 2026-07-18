using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace MotorcycleRAG.DbSetup;

public enum DbSetupScript
{
    Schema,
    TestData
}

public sealed class SqlScriptExecutor
{
    private static readonly IReadOnlyDictionary<DbSetupScript, string> ApprovedScriptRelativePaths =
        new Dictionary<DbSetupScript, string>
        {
            [DbSetupScript.Schema] = Path.Combine("4-Persistence", "MotorcycleRAG.Persistence", "Sql", "schema.sql"),
            [DbSetupScript.TestData] = Path.Combine("7-Deployment", "DbSetup", "sql", "test-data.sql")
        };

    private readonly ILogger<SqlScriptExecutor> _logger;
    private readonly IDbSetupConnectionFactory _connectionFactory;
    private readonly string _catalogRoot;

    public SqlScriptExecutor(
        ILogger<SqlScriptExecutor> logger,
        IDbSetupConnectionFactory connectionFactory,
        string catalogRoot)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        ArgumentNullException.ThrowIfNull(catalogRoot);

        _catalogRoot = Path.GetFullPath(catalogRoot);
    }

    public async Task<bool> ExecuteScriptAsync(
        string connectionString,
        DbSetupScript script,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetApprovedScriptPath(script, out var scriptPath))
        {
            _logger.LogError("Rejected SQL setup script {Script}", script);
            return false;
        }

        if (!File.Exists(scriptPath))
        {
            _logger.LogError("SQL setup script {Script} was not found in the catalog", script);
            return false;
        }

        if (ContainsSymbolicLink(scriptPath))
        {
            _logger.LogError("Rejected SQL setup script {Script} because its catalog path contains a symbolic link", script);
            return false;
        }

        try
        {
            var scriptContent = await File.ReadAllTextAsync(scriptPath, cancellationToken);
            var batches = SplitScriptIntoBatches(scriptContent);

            _logger.LogInformation("Executing approved SQL setup script {Script} with {BatchCount} batches", script, batches.Count);

            await using var connection = _connectionFactory.Create(connectionString);
            await connection.OpenAsync(cancellationToken);

            for (var batchIndex = 0; batchIndex < batches.Count; batchIndex++)
            {
                try
                {
                    await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Batch comes only from an approved, non-symlinked catalog file.
                    command.CommandText = batches[batchIndex];
#pragma warning restore CA2100
                    command.CommandTimeout = 300;

                    await command.ExecuteNonQueryAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to execute batch {BatchNumber} for SQL setup script {Script}", batchIndex + 1, script);
                    return false;
                }
            }

            _logger.LogInformation("Successfully executed approved SQL setup script {Script}", script);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute SQL setup script {Script}", script);
            return false;
        }
    }

    private bool TryGetApprovedScriptPath(DbSetupScript script, out string scriptPath)
    {
        scriptPath = string.Empty;

        if (!ApprovedScriptRelativePaths.TryGetValue(script, out var relativePath))
        {
            return false;
        }

        var candidatePath = Path.GetFullPath(Path.Combine(_catalogRoot, relativePath));
        if (!IsContainedInRoot(candidatePath, _catalogRoot))
        {
            return false;
        }

        scriptPath = candidatePath;
        return true;
    }

    private bool ContainsSymbolicLink(string scriptPath)
    {
        if (IsSymbolicLink(new DirectoryInfo(_catalogRoot)))
        {
            return true;
        }

        var relativePath = Path.GetRelativePath(_catalogRoot, scriptPath);
        var pathSegments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var currentPath = _catalogRoot;

        for (var index = 0; index < pathSegments.Length; index++)
        {
            currentPath = Path.Combine(currentPath, pathSegments[index]);
            FileSystemInfo pathEntry = index == pathSegments.Length - 1
                ? new FileInfo(currentPath)
                : new DirectoryInfo(currentPath);

            if (IsSymbolicLink(pathEntry))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsContainedInRoot(string path, string root)
    {
        var relativePath = Path.GetRelativePath(root, path);
        return !string.IsNullOrEmpty(relativePath)
            && !Path.IsPathRooted(relativePath)
            && !relativePath.Equals("..", StringComparison.Ordinal)
            && !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static bool IsSymbolicLink(FileSystemInfo pathEntry) => pathEntry.LinkTarget is not null;

    private static List<string> SplitScriptIntoBatches(string scriptContent) =>
        Regex.Split(scriptContent, @"^\s*GO\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline)
            .Select(batch => batch.Trim())
            .Where(batch => !string.IsNullOrWhiteSpace(batch))
            .ToList();
}
