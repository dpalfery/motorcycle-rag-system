# Motorcycle RAG Database Setup CLI

## Overview

The Motorcycle RAG Database Setup CLI is a .NET 10 console application that automates the provisioning of SQL Server databases for the Motorcycle RAG System. It connects to an existing SQL Server instance (running in Docker) and creates the database, schema, and application user.

## Internal Boundary

`Program` is the CLI composition root. It creates the CLI-local `SqlDbSetupConnectionFactory`, the only production component that constructs `SqlConnection`; the provisioner, preflight checks, and schema runner receive `DbConnection` instances through that factory. This keeps SQL connection ownership explicit and allows the test suite to use fake connections without contacting a server. See the [database setup reference](../../6-Docs/DevOps/database-setup.md) for operational scope.

## Features

- **Works with existing SQL Server**: Connects to existing hotshot_sqlserver container at localhost:1433
- **Database provisioning**: Creates database, login, and user with appropriate permissions
- **Schema deployment**: Executes schema.sql to create all tables, indexes, and stored procedures
- **Test data seeding**: Optional test data insertion for development
- **Interactive and non-interactive modes**: Supports both developer workflows and CI/CD pipelines
- **Secure password generation**: Cryptographically secure password generation for application user
- **Environment variable management**: Automatic persistence of database credentials
- **Comprehensive logging**: Structured logging for troubleshooting

## Prerequisites

- .NET 10 SDK
- SQL Server instance running in Docker (hotshot_sqlserver container on localhost:1433)
- SA password for the SQL Server instance

## Quick Start

### Interactive Mode (Recommended for Development)

```powershell
# Navigate to the project directory
cd 7-Deployment/DbSetup/MotorcycleRAG.DbSetup

# Run with interactive prompts
dotnet run
```

You'll be prompted for:
- SA password
- Database name (default: MotorcycleRAG)
- Application user name (default: motorcyclerag_app)

### Non-Interactive Mode (CI/CD)

```powershell
dotnet run --non-interactive `
  --sa-password "YourSaPassword" `
  --db-name "MotorcycleRAG" `
  --app-user "motorcyclerag_app"
```

### Seed Test Data

```powershell
dotnet run --seed-test-data
```

## Command Line Options

| Option | Description | Default | Example |
|--------|-------------|---------|---------|
| `--server`, `-s` | SQL Server instance | `localhost` | `localhost` |
| `--port` | SQL Server port | `1433` | `1433` |
| `--sa-password` | SA password for SQL Server | Required | `MyPassword123!` |
| `--db-name` | Target database name | `MotorcycleRAG` | `MotorcycleRAG` |
| `--app-user` | Application user/login name | `motorcyclerag_app` | `motorcyclerag_app` |
| `--app-password` | Application user password | Auto-generated | `MyAppPassword123!` |
| `--non-interactive` | Run without prompts | `false` | `--non-interactive` |
| `--seed-test-data` | Seed database with test data | `false` | `--seed-test-data` |
| `--env-vars-in-proc` | Set env vars at process level (for CI/CD) | `false` | `--env-vars-in-proc` |
| `--project-slug` | Project identifier for environment variables | `motorcyclerag` | `motorcyclerag` |

## Environment Variables

The tool sets the following environment variables:

| Variable | Description |
|----------|-------------|
| `MOTORCYCLERAG_DB_SERVER` | SQL Server instance |
| `MOTORCYCLERAG_DB_PORT` | SQL Server port |
| `MOTORCYCLERAG_DB_NAME` | Database name |
| `MOTORCYCLERAG_DB_APP_USER` | Application user name |
| `MOTORCYCLERAG_DB_APP_PASSWORD` | Application user password |
| `MOTORCYCLERAG_DB_SA_PASSWORD` | SA password |
| `ConnectionStrings__DefaultConnection` | Full connection string |

## What It Does

1. **Validates inputs**: Checks all required parameters
2. **Verifies Docker container**: Confirms SQL Server is running at localhost:1433
3. **Runs preflight checks**: Tests connectivity and permissions
4. **Creates database**: Creates MotorcycleRAG database if it doesn't exist
5. **Creates login and user**: Sets up application user with secure password
6. **Grants permissions**: Grants SELECT, INSERT, UPDATE, DELETE, EXECUTE on dbo schema
7. **Deploys schema**: Executes schema.sql to create all database objects
8. **Seeds test data** (optional): Inserts sample data for development
9. **Sets environment variables**: Persists credentials for application use

## Database Schema

The CLI deploys the complete Motorcycle RAG schema including:

- **Tables**: Users, UserPlans, Usage, WebSources, WebSourceCrawlResults, AuditLogs, IngestionJobs, WebTrustPolicies, ToolConfigurations, ToolConfigurationAuditLog
- **Stored Procedures**: Query and reporting procedures
- **Indexes**: Performance-optimized indexes
- **Foreign Keys**: Referential integrity constraints

## Test Data

When `--seed-test-data` is specified, the CLI inserts:

- 3 user plans (Free, Plus, Pro)
- 3 test users (one for each plan)
- 3 trusted web sources (Cycle World, Motorcycle.com, RevZilla)
- 2 sample tool configurations (Web Search, Vector Search)
- Sample audit log entries

## Troubleshooting

### Connection Timeout

```
Error: SQL Server connectivity failed
```

**Solution**: Ensure the hotshot_sqlserver container is running:
```powershell
docker ps | findstr hotshot_sqlserver
```

### Permission Denied

```
Error: Privileged credentials check failed
```

**Solution**: Verify you're using the correct SA password.

### Schema Deployment Failed

```
Error: Failed to deploy database schema
```

**Solution**: Check that schema.sql exists at `4-Persistence/MotorcycleRAG.Persistence/Sql/schema.sql`

## Connection String Output

After successful setup, the CLI outputs:

```
Connection string:
Server=localhost,1433;Database=MotorcycleRAG;User Id=motorcyclerag_app;Password=<hidden>;TrustServerCertificate=true;

Environment variables set (using project slug 'MOTORCYCLERAG'):
MOTORCYCLERAG_DB_SERVER=localhost
MOTORCYCLERAG_DB_PORT=1433
MOTORCYCLERAG_DB_NAME=MotorcycleRAG
MOTORCYCLERAG_DB_APP_USER=motorcyclerag_app
MOTORCYCLERAG_DB_APP_PASSWORD=<set>
MOTORCYCLERAG_DB_SA_PASSWORD=<set>
ConnectionStrings__DefaultConnection=<set>
```

## Security Notes

⚠️ **Important Security Considerations:**

1. **Never commit SA passwords** to source control
2. **Use environment variables** or secure secret management
3. **SA passwords must meet** SQL Server complexity requirements
4. **Application passwords** are auto-generated with cryptographic security (24 characters by default)
5. **Connection strings** contain passwords - handle them securely

## Support

For issues:
1. Check the troubleshooting section above
2. Review application logs for detailed error messages
3. Verify SQL Server is running and accessible
4. Ensure you have the correct SA password
