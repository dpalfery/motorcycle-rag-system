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

        // CodeQL cs/insecure-sql-connection recognizes legacy Encrypt=True (indexer/bool),
        // not SqlConnectionEncryptOption.Mandatory alone. "True" maps to Mandatory in
        // Microsoft.Data.SqlClient 5.x; TrustServerCertificate stays false so the
        // server certificate is still validated.
        var builder = new SqlConnectionStringBuilder(connectionString);
        builder["Encrypt"] = "True";
        builder.TrustServerCertificate = false;

        return new SqlConnection(builder.ConnectionString);
    }
}
