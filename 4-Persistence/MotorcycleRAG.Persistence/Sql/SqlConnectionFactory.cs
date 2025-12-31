using System;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Core.Options; 


namespace MotorcycleRAG.Persistence.Sql
{
    /// <summary>
    /// Factory for creating SQL database connections
    /// Connection string is retrieved from SQL_CONNECTION_STRING environment variable
    /// </summary>
    public class SqlConnectionFactory : ISqlConnectionFactory
    {
        private readonly string _connectionString;
        private readonly SqlOptions _sqlOptions;
        private readonly ILogger<SqlConnectionFactory> _logger;

        /// <summary>
        /// Initializes a new instance of the SqlConnectionFactory
        /// </summary>
        /// <param name="sqlOptions">SQL configuration options</param>
        /// <param name="logger">Logger</param>
        public SqlConnectionFactory(IOptions<SqlOptions> sqlOptions, ILogger<SqlConnectionFactory> logger)
        {
            _sqlOptions = sqlOptions.Value ?? throw new ArgumentNullException(nameof(sqlOptions));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            var connectionString = Environment.GetEnvironmentVariable("SQL_CONNECTION_STRING");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "SQL_CONNECTION_STRING environment variable is required but not set. " +
                    "Please set this environment variable before starting the application.");
            }

            // Enforce policy: connection string must not contain embedded credentials
            // Azure AD / Managed Identity authentication is required
            var upperConnectionString = connectionString.ToUpperInvariant();
            if (upperConnectionString.Contains("PASSWORD=") ||
                upperConnectionString.Contains("PWD=") ||
                upperConnectionString.Contains("USER ID=") ||
                upperConnectionString.Contains("UID="))
            {
                throw new InvalidOperationException(
                    "SQL_CONNECTION_STRING must not contain embedded credentials (Password, Pwd, User ID, or UID). " +
                    "Azure AD / Managed Identity authentication is required. " +
                    "Please configure your connection string to use Azure AD authentication.");
            }

            _connectionString = connectionString;
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
                _logger.LogError("Failed to create SQL connection. Error type: {ErrorType}", ex.GetType().Name);
                throw;
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
                _logger.LogError("Failed to create SQL connection. Error type: {ErrorType}", ex.GetType().Name);
                throw;
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
                await ((SqlConnection)connection).OpenAsync();
                _logger.LogDebug("Opened SQL connection");
                return connection;
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to open SQL connection. Error type: {ErrorType}", ex.GetType().Name);
                connection.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Creates a new SQL command with the specified connection and transaction
        /// </summary>
        /// <param name="connection">SQL connection</param>
        /// <param name="transaction">SQL transaction</param>
        /// <param name="commandText">Command text</param>
        /// <param name="commandType">Command type</param>
        /// <param name="commandTimeout">Command timeout</param>
        /// <returns>SQL command</returns>
        public IDbCommand CreateCommand(
            IDbConnection connection,
            IDbTransaction? transaction = null,
            string? commandText = null,
            CommandType commandType = CommandType.Text,
            int? commandTimeout = null)
        {
            if (connection == null)
            {
                throw new ArgumentNullException(nameof(connection));
            }

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
        /// <param name="dbType">Parameter data type</param>
        /// <param name="direction">Parameter direction</param>
        /// <returns>SQL parameter</returns>
        public IDataParameter CreateParameter(
            string parameterName,
            object? value,
            DbType dbType = DbType.String,
            ParameterDirection direction = ParameterDirection.Input)
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
        IDbCommand CreateCommand(
            IDbConnection connection,
            IDbTransaction? transaction = null,
            string? commandText = null,
            CommandType commandType = CommandType.Text,
            int? commandTimeout = null);
        IDataParameter CreateParameter(
            string parameterName,
            object? value,
            DbType dbType = DbType.String,
            ParameterDirection direction = ParameterDirection.Input);
    }
}