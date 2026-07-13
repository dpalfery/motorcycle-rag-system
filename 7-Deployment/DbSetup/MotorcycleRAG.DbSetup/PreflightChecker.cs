using System.Data;
using Microsoft.Extensions.Logging;

namespace MotorcycleRAG.DbSetup;

public class PreflightChecker
{
    private readonly ILogger<PreflightChecker> _logger;
    private readonly IDbSetupConnectionFactory _connectionFactory;

    public PreflightChecker(ILogger<PreflightChecker> logger, IDbSetupConnectionFactory connectionFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<bool> PerformPreflightChecksAsync(string connectionString, string databaseName, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting preflight checks for database: {DatabaseName}", databaseName);

        var checks = new List<(string Name, Func<Task<bool>> Check, bool Optional)>
        {
            ("SQL Server Connectivity", () => CheckSqlServerConnectivityAsync(connectionString, cancellationToken), false),
            ("Privileged Credentials", () => CheckPrivilegedCredentialsAsync(connectionString, cancellationToken), false)
        };

        var allPassed = true;

        foreach (var (name, check, optional) in checks)
        {
            try
            {
                _logger.LogDebug("Running preflight check: {CheckName}", name);
                var passed = await check();

                if (passed)
                {
                    _logger.LogInformation("✓ {CheckName} passed", name);
                }
                else
                {
                    if (optional)
                    {
                        _logger.LogWarning("⚠ {CheckName} failed (optional - will be created)", name);
                    }
                    else
                    {
                        _logger.LogError("✗ {CheckName} failed", name);
                        allPassed = false;
                    }
                }
            }
            catch (Exception ex)
            {
                if (optional)
                {
                    _logger.LogWarning("⚠ {CheckName} failed (optional - will be created). ExceptionType={ExceptionType}", name, ex.GetType().Name);
                }
                else
                {
                    _logger.LogError("✗ {CheckName} failed with exception. ExceptionType={ExceptionType}", name, ex.GetType().Name);
                    allPassed = false;
                }
            }
        }

        if (allPassed)
        {
            _logger.LogInformation("All preflight checks passed");
        }
        else
        {
            _logger.LogError("Some preflight checks failed. Please review the errors above.");
        }

        return allPassed;
    }

    private async Task<bool> CheckSqlServerConnectivityAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = _connectionFactory.Create(connectionString);
            await connection.OpenAsync(cancellationToken);

            // Test with a simple query
            const string query = "SELECT SERVERPROPERTY('ProductVersion')";
            await using var command = connection.CreateCommand();
            command.CommandText = query;
            var result = await command.ExecuteScalarAsync(cancellationToken);

            _logger.LogDebug("SQL Server connectivity test successful. Version: {Version}", result);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error during SQL Server connectivity check. ExceptionType={ExceptionType}", ex.GetType().Name);
            return false;
        }
    }

    private async Task<bool> CheckPrivilegedCredentialsAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = _connectionFactory.Create(connectionString);
            await connection.OpenAsync(cancellationToken);

            // Check if we have sysadmin or sufficient privileges
            const string query = @"
                SELECT
                    IS_SRVROLEMEMBER('sysadmin') as IsSysAdmin,
                    IS_SRVROLEMEMBER('serveradmin') as IsServerAdmin,
                    HAS_PERMS_BY_NAME(null, null, 'CREATE ANY DATABASE') as CanCreateDatabase,
                    HAS_PERMS_BY_NAME(null, null, 'ALTER ANY LOGIN') as CanAlterLogin";

            await using var command = connection.CreateCommand();
            command.CommandText = query;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            if (await reader.ReadAsync(cancellationToken))
            {
                // SQL Server returns 1 or 0 (int), not boolean
                var isSysAdmin = reader.GetInt32(0) == 1;
                var isServerAdmin = reader.GetInt32(1) == 1;
                var canCreateDatabase = reader.GetInt32(2) == 1;
                var canAlterLogin = reader.GetInt32(3) == 1;

                _logger.LogDebug("Privilege check results: SysAdmin={IsSysAdmin}, ServerAdmin={IsServerAdmin}, CanCreateDatabase={CanCreateDatabase}, CanAlterLogin={CanAlterLogin}",
                    isSysAdmin, isServerAdmin, canCreateDatabase, canAlterLogin);

                // We need either sysadmin, serveradmin, or specific permissions
                return isSysAdmin || isServerAdmin || (canCreateDatabase && canAlterLogin);
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error during privileged credentials check. ExceptionType={ExceptionType}", ex.GetType().Name);
            return false;
        }
    }

    public string GetRemediationInstructions()
    {
        return @"
To resolve privilege issues:

1. Ensure you're using SQL Server authentication with sufficient privileges
2. Use 'sa' account or an account with:
   - sysadmin role, OR
   - serveradmin role, OR
   - Both CREATE ANY DATABASE and ALTER ANY LOGIN permissions

3. For Windows authentication, ensure your Windows account has sufficient SQL Server privileges

4. In non-interactive mode, provide --sa-password with privileged credentials

Example connection strings:
- SQL Server: Server=localhost;Database=master;User Id=sa;Password=mypassword;TrustServerCertificate=true;
";
    }
}
