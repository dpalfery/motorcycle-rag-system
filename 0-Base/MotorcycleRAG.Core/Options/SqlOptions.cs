namespace MotorcycleRAG.Core.Options
{
    /// <summary>
    /// SQL configuration options
    /// </summary>
    public class SqlOptions
    {
        /// <summary>
        /// SQL Server connection string.
        /// Populated from IConfiguration (App Config + Key Vault in Azure, user-secrets in dev).
        /// </summary>
        public string ConnectionString { get; set; } = string.Empty;

        /// <summary>
        /// Command timeout in seconds
        /// </summary>
        public int CommandTimeout { get; set; } = 60;

        /// <summary>
        /// Connection timeout in seconds
        /// </summary>
        public int ConnectionTimeout { get; set; } = 30;

        /// <summary>
        /// Maximum pool size
        /// </summary>
        public int MaxPoolSize { get; set; } = 100;
    }
}