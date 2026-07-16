using System;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Core.Options;
using Polly; 


namespace MotorcycleRAG.Persistence.Sql
{
    /// <summary>
    /// Factory for creating SQL database connections
    /// Connection string is retrieved from IOptions<SqlOptions> (Sql:ConnectionString configuration key)
    /// </summary>
    public class SqlConnectionFactory : ISqlConnectionFactory
    {
        private readonly string _connectionString;
        private readonly SqlOptions _sqlOptions;
        private readonly ILogger<SqlConnectionFactory> _logger;
        private static readonly IAsyncPolicy _sqlRetryPolicy = Policy
            .Handle<SqlException>(ex =>
                ex.Number is 4060 or 40197 or 40501 or 40613 or 49918 or 49919 or 49920 or 4221 or 18456 or 18470)
            .Or<TimeoutException>()
            .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)));

        /// <summary>
        /// Initializes a new instance of the SqlConnectionFactory
        /// </summary>
        /// <param name="sqlOptions">SQL configuration options</param>
        /// <param name="logger">Logger</param>
        public SqlConnectionFactory(IOptions<SqlOptions> sqlOptions, ILogger<SqlConnectionFactory> logger)
        {
            ArgumentNullException.ThrowIfNull(sqlOptions);
            ArgumentNullException.ThrowIfNull(logger);

            _sqlOptions = sqlOptions.Value ?? throw new ArgumentNullException(nameof(sqlOptions));
            _logger = logger;

            var connectionString = _sqlOptions.ConnectionString;
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "SQL connection string is not configured in Sql:ConnectionString. " +
                    "Provide it through Azure App Configuration + Key Vault.");
            }

            // Enforce policy: connection string must not contain embedded credentials
            // Azure AD / Managed Identity authentication is required
            var upperConnectionString = connectionString.ToUpperInvariant();
            if (upperConnectionString.Contains("PASSWORD=") ||
                upperConnectionString.Contains("PWD=") ||
                upperConnectionString.Contains("USER ID=") ||
                upperConnectionString.Contains("UID="))
            {
                _logger.LogWarning(
                    "Sql:ConnectionString contains embedded credentials. This is only permitted when the value is delivered through Azure App Configuration + Key Vault.");
            }

            var builder = new SqlConnectionStringBuilder(connectionString)
            {
                MaxPoolSize = _sqlOptions.MaxPoolSize,
                ConnectTimeout = _sqlOptions.ConnectionTimeout,
            };
            // Add Connection Lifetime to recycle dead connections every 5 minutes
            builder["Connection Lifetime"] = 300;
            _connectionString = builder.ConnectionString;
        }

        /// <summary>
        /// Creates a new SQL connection
        /// </summary>
        /// <returns>SQL connection</returns>
        public IDbConnection CreateConnection()
        {
            try
            {
                var connection = new SqlConnection(_connectionString);
                _logger.LogDebug("Created SQL connection");
                return connection;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create SQL connection");
                throw new InvalidOperationException("An error occurred while creating a SQL connection.", ex);
            }
        }

        /// <summary>
        /// Creates a new SQL connection asynchronously
        /// </summary>
        /// <returns>SQL connection</returns>
        public async Task<IDbConnection> CreateConnectionAsync()
        {
            try
            {
                var connection = new SqlConnection(_connectionString);
                _logger.LogDebug("Created SQL connection");
                return connection;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create SQL connection");
                throw new InvalidOperationException("An error occurred while creating a SQL connection asynchronously.", ex);
            }
        }

        /// <summary>
        /// Creates a new SQL connection and opens it
        /// </summary>
        /// <returns>Open SQL connection</returns>
        public async Task<IDbConnection> CreateOpenConnectionAsync()
        {
            var connection = await CreateConnectionAsync();
            try
            {
                // Retry on Azure SQL transient errors (4060, 40197, 40501, etc.)
                await _sqlRetryPolicy.ExecuteAsync(async () =>
                {
                    await OpenSqlConnectionCore((SqlConnection)connection);
                });
                _logger.LogDebug("Opened SQL connection");
                return connection;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open SQL connection");
                connection.Dispose();
                throw new InvalidOperationException("An error occurred while opening a SQL connection.", ex);
            }
        }

        /// <summary>
        /// Opens the SQL connection. Overridable in tests so the catch-block logic
        /// (disposal, error logging, InvalidOperationException wrapping) is
        /// testable without a real SQL Server instance.
        /// </summary>
        /// <param name="connection">The SQL connection to open.</param>
        protected virtual async Task OpenSqlConnectionCore(SqlConnection connection)
        {
            await connection.OpenAsync();
        }

        /// <summary>
        /// Creates a new SQL command with the specified connection
        /// </summary>
        /// <param name="connection">SQL connection</param>
        /// <returns>SQL command</returns>
        public IDbCommand CreateCommand(IDbConnection connection)
        {
            return CreateCommand(connection, null, null, CommandType.Text, null);
        }

        /// <summary>
        /// Creates a new SQL command with the specified connection and transaction
        /// </summary>
        /// <param name="connection">SQL connection</param>
        /// <param name="transaction">SQL transaction</param>
        /// <returns>SQL command</returns>
        public IDbCommand CreateCommand(IDbConnection connection, IDbTransaction? transaction)
        {
            return CreateCommand(connection, transaction, null, CommandType.Text, null);
        }

        /// <summary>
        /// Creates a new SQL command with the specified connection and command text
        /// </summary>
        /// <param name="connection">SQL connection</param>
        /// <param name="commandText">Command text</param>
        /// <returns>SQL command</returns>
        public IDbCommand CreateCommand(IDbConnection connection, string commandText)
        {
            return CreateCommand(connection, null, commandText, CommandType.Text, null);
        }

        /// <summary>
        /// Creates a new SQL command with full options
        /// </summary>
        /// <param name="connection">SQL connection</param>
        /// <param name="transaction">SQL transaction</param>
        /// <param name="commandText">Command text</param>
        /// <param name="commandType">Command type</param>
        /// <param name="commandTimeout">Command timeout</param>
        /// <returns>SQL command</returns>
        public IDbCommand CreateCommand(
            IDbConnection connection,
            IDbTransaction? transaction,
            string? commandText,
            CommandType commandType,
            int? commandTimeout)
        {
            ArgumentNullException.ThrowIfNull(connection);

            var command = connection.CreateCommand();
            if (command == null)
            {
                throw new InvalidOperationException("Connection failed to create command");
            }

            if (transaction != null)
            {
                command.Transaction = transaction;
            }

            if (commandText != null)
            {
                command.CommandText = commandText;
            }

            command.CommandType = commandType;

            if (commandTimeout.HasValue)
            {
                command.CommandTimeout = commandTimeout.Value;
            }
            else if (_sqlOptions.CommandTimeout > 0)
            {
                command.CommandTimeout = _sqlOptions.CommandTimeout;
            }

            return command;
        }

        /// <summary>
        /// Creates a new SQL parameter
        /// </summary>
        /// <param name="parameterName">Parameter name</param>
        /// <param name="value">Parameter value</param>
        /// <returns>SQL parameter</returns>
        public IDataParameter CreateParameter(string parameterName, object? value)
        {
            return CreateParameter(parameterName, value, DbType.String, ParameterDirection.Input);
        }

        /// <summary>
        /// Creates a new SQL parameter with specified type
        /// </summary>
        /// <param name="parameterName">Parameter name</param>
        /// <param name="value">Parameter value</param>
        /// <param name="dbType">Parameter data type</param>
        /// <returns>SQL parameter</returns>
        public IDataParameter CreateParameter(string parameterName, object? value, DbType dbType)
        {
            return CreateParameter(parameterName, value, dbType, ParameterDirection.Input);
        }

        /// <summary>
        /// Creates a new SQL parameter with full options
        /// </summary>
        /// <param name="parameterName">Parameter name</param>
        /// <param name="value">Parameter value</param>
        /// <param name="dbType">Parameter data type</param>
        /// <param name="direction">Parameter direction</param>
        /// <returns>SQL parameter</returns>
        public IDataParameter CreateParameter(
            string parameterName,
            object? value,
            DbType dbType,
            ParameterDirection direction)
        {
            var parameter = new SqlParameter
            {
                ParameterName = parameterName,
                Value = value ?? DBNull.Value,
                DbType = dbType,
                Direction = direction
            };

            return parameter;
        }
    }

    /// <summary>
    /// Interface for SQL connection factory
    /// </summary>
    public interface ISqlConnectionFactory
    {
        IDbConnection CreateConnection();
        Task<IDbConnection> CreateConnectionAsync();
        Task<IDbConnection> CreateOpenConnectionAsync();

        IDbCommand CreateCommand(IDbConnection connection);
        IDbCommand CreateCommand(IDbConnection connection, IDbTransaction? transaction);
        IDbCommand CreateCommand(IDbConnection connection, string commandText);
        IDbCommand CreateCommand(
            IDbConnection connection,
            IDbTransaction? transaction,
            string? commandText,
            CommandType commandType,
            int? commandTimeout);

        IDataParameter CreateParameter(string parameterName, object? value);
        IDataParameter CreateParameter(string parameterName, object? value, DbType dbType);
        IDataParameter CreateParameter(
            string parameterName,
            object? value,
            DbType dbType,
            ParameterDirection direction);
    }
}
