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

        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            Encrypt = SqlConnectionEncryptOption.Mandatory,
            TrustServerCertificate = false
        };

        return new SqlConnection(builder.ConnectionString);
    }
}
