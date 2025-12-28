namespace MotorcycleRAG.Contracts.Options
{
    /// <summary>
    /// SQL configuration options
    /// Note: Connection string must be provided via SQL_CONNECTION_STRING environment variable
    /// </summary>
    public class SqlOptions
    {
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