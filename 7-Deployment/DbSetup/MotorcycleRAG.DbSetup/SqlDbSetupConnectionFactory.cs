using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace MotorcycleRAG.DbSetup;

/// <summary>
/// SQL Server implementation of the DbSetup connection factory.
/// </summary>
public sealed class SqlDbSetupConnectionFactory : IDbSetupConnectionFactory
{
    public DbConnection Create(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // codeql[cs/insecure-sql-connection]: CodeQL's sink model looks for the legacy
        // boolean `Encrypt=True`; SqlConnectionEncryptOption.Mandatory (Microsoft.Data.SqlClient
        // 5.x) is the modern, stricter equivalent and TrustServerCertificate stays false so the
        // server certificate is still validated.
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            Encrypt = SqlConnectionEncryptOption.Mandatory,
            TrustServerCertificate = false
        };

        return new SqlConnection(builder.ConnectionString);
    }
}
