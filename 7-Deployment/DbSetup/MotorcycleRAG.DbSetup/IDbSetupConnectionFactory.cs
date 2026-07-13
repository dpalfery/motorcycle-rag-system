using System.Data.Common;

namespace MotorcycleRAG.DbSetup;

/// <summary>
/// Creates database connections for the DbSetup command-line workflow.
/// </summary>
public interface IDbSetupConnectionFactory
{
    DbConnection Create(string connectionString);
}
