using System;

namespace MotorcycleRAG.Contracts.Options
{
    /// <summary>
    /// SQL configuration options
    /// </summary>
    public class SqlOptions
    {
        /// <summary>
        /// SQL Server connection string
        /// </summary>
        public string ConnectionString { get; set; } = string.Empty;

        /// <summary>
        /// SQL Server server name
        /// </summary>
        public string Server { get; set; } = string.Empty;

        /// <summary>
        /// SQL Server database name
        /// </summary>
        public string Database { get; set; } = string.Empty;

        /// <summary>
        /// SQL Server username
        /// </summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// SQL Server password
        /// </summary>
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// Connection timeout in seconds
        /// </summary>
        public int ConnectionTimeout { get; set; } = 30;

        /// <summary>
        /// Command timeout in seconds
        /// </summary>
        public int CommandTimeout { get; set; } = 60;

        /// <summary>
        /// Maximum pool size
        /// </summary>
        public int MaxPoolSize { get; set; } = 100;

        /// <summary>
        /// Whether to use integrated security
        /// </summary>
        public bool UseIntegratedSecurity { get; set; } = false;
    }
}