using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Persistence.Sql;

namespace MotorcycleRAG.Persistence.HealthChecks;

/// <summary>
/// Health check for SQL Database connectivity and availability
/// </summary>
public class SqlDatabaseHealthCheck : IHealthCheck
{
    private readonly ISqlConnectionFactory _connectionFactory;
    private readonly ILogger<SqlDatabaseHealthCheck> _logger;

    public SqlDatabaseHealthCheck(ISqlConnectionFactory connectionFactory, ILogger<SqlDatabaseHealthCheck> logger)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var startTime = DateTime.UtcNow;
            
            // Get a connection from the factory and test it
            await using (var connection = await _connectionFactory.CreateConnectionAsync() as SqlConnection)
            {
                if (connection == null)
                {
                    return HealthCheckResult.Unhealthy("Failed to create SQL connection");
                }

                await connection.OpenAsync(cancellationToken);
                
                // Execute a simple query to verify database is responding
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "SELECT 1";
                    await cmd.ExecuteScalarAsync(cancellationToken);
                }
            }

            var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
            
            _logger.LogInformation("SQL Database health check passed in {Duration}ms", duration);
            
            return HealthCheckResult.Healthy("SQL Database is healthy",
                new Dictionary<string, object> { { "response_time_ms", duration } });
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "SQL Database health check timed out");
            return HealthCheckResult.Unhealthy("SQL Database health check timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SQL Database health check failed");
            return HealthCheckResult.Unhealthy($"SQL Database health check failed: {ex.Message}", ex);
        }
    }
}
