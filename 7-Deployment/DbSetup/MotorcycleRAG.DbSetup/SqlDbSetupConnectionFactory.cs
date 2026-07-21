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

        // The caller-supplied string is never handed to a constructor directly: it is
        // parsed by the builder first, and the two settings below are applied on top of
        // whatever it contained. "true" maps to SqlConnectionEncryptOption.Mandatory in
        // Microsoft.Data.SqlClient 5.x, and TrustServerCertificate stays false so the
        // server certificate is still validated. Initializer members run in order, so
        // the encryption settings always win over the incoming string.
        var builder = new SqlConnectionStringBuilder
        {
            ConnectionString = connectionString,
            Encrypt = true,
            TrustServerCertificate = false,
        };

        var connection = new SqlConnection();
        connection.ConnectionString = builder.ConnectionString;
        return connection;
    }
}
